namespace AskTheCode.SymbolicExecution

open AskTheCode.Smt
open AskTheCode.Cfg
open AskTheCode.Heap

module Exploration =
    open AskTheCode

    // Definition of functions for heap handling and default value

    type HeapFunctions<'heap> = { GetEmpty: unit -> 'heap; PerformOp: HeapOperation -> 'heap -> 'heap * Term option }

    let unsupportedHeapFn : HeapFunctions<unit> = { GetEmpty = id; PerformOp = (fun _ _ -> failwith "Heap unsupported") }

    // Definition of functions for path condition handling and the simplest implementation based on solver function

    type ConditionFunctions<'condition> = { GetEmpty: unit -> 'condition; Assert: Term -> 'condition -> 'condition; Solve: 'condition -> SolveResult }

    let solverCondFn solver :ConditionFunctions<Term> = { GetEmpty = (fun () -> BoolConst true); Assert = Utils.curry2 And; Solve = solver }

    let assertCondition condFn cond term =
        match term with
        | BoolConst true -> cond
        | _ -> condFn.Assert term cond

    // Variable version handling

    let getVersion versions name =
        Map.tryFind name versions |> Option.defaultValue 0

    let formatVersioned name version =
        sprintf "%s!%d" name version

    let addVersion versions (variable:Variable) =
        let version = getVersion versions variable.Name
        { variable with Name = formatVersioned variable.Name version }

    let rec addVersions versions term =
        match term with
        | Var v ->
            Var <| addVersion versions v
        | _ ->
            Term.updateChildren (addVersions versions) term

    let mergeVersions versions1 versions2 =
        let mergeItem res name version =
            match Map.tryFind name res with
            | Some currentVersion when currentVersion < version ->
                Map.add name version res
            | None ->
                Map.add name version res
            | _ ->
                res
        Map.fold mergeItem versions2 versions1      // The order to ease folding with Map.empty

    let addHeapOpVersions versions heapOp =
        match heapOp with
        | AssignEquals (trg, left, right) ->
            AssignEquals (addVersion versions trg, left, right)
        | AssignNotEquals (trg, left, right) ->
            AssignNotEquals (addVersion versions trg, left, right)
        | ReadVal (trg, ins, field) ->
            ReadVal (addVersion versions trg, ins, field)
        | WriteVal (ins, field, value) ->
            WriteVal (ins, field, addVersions versions value)
        | _ ->
            heapOp

    // Processing operations into path constraints, version changes and heap updates

    let processOperation heapFn op versions heap =
        match op with
        | (Assign assign) ->
            let trgName = assign.Target.Name
            let trgVersion = getVersion versions trgName
            let target = Var { assign.Target with Name = formatVersioned trgName trgVersion }
            let versions = Map.add trgName (trgVersion + 1) versions
            let value = addVersions versions assign.Value
            (Some (Eq (target, value)), versions, heap)
        | (HeapOp heapOp) ->
            let versionedHeapOp = addHeapOpVersions versions heapOp
            let (heap, heapCond) = heapFn.PerformOp versionedHeapOp heap
            let versions =
                match HeapOperation.targetVariable heapOp with
                | Some { Name = varName } ->
                    let curVersion = getVersion versions varName
                    Map.add varName (curVersion + 1) versions
                | None ->
                    versions
            (heapCond, versions, heap)

    let processNode heapFn node versions heap =
        let folder op (cond, versions, heap) =
            let (opCond, versions, heap) = processOperation heapFn op versions heap
            (Utils.mergeOptions (Utils.curry2 And) cond opCond, versions, heap)
        match node with
        | Basic (_, operations) ->
            List.foldBack folder operations (None, versions, heap)
        | _ ->
            (None, versions, heap)

    // Explore each path separately

    type ExplorerState<'condition, 'heap> = { Path: Path; Condition: 'condition; Versions: Map<string, int>; Heap: 'heap }

    let run condFn heapFn graph targetNode =
        let extend graph state (edge:Edge) =
            match edge with
            | Inner innerEdge ->
                let node = Graph.node graph innerEdge.From
                let path = Step (node, edge, state.Path)
                let cond =
                    addVersions state.Versions innerEdge.Condition
                    |> assertCondition condFn state.Condition
                let (nodeCond, versions, heap) = processNode heapFn node state.Versions state.Heap
                let cond = Option.fold (assertCondition condFn) cond nodeCond
                { state with Path = path; Condition = cond; Versions = versions; Heap = heap }
            | Outer _ ->
                failwith "Not implemented"

        let rec step states results =
            match states with
            | [] ->
                results
            | state :: states' ->
                let node = Path.node state.Path
                match condFn.Solve state.Condition with
                | Sat _ ->
                    match node with
                    | Enter _ ->
                        step states' (state.Path :: results)
                    | _ ->
                        let states'' =
                            Graph.edgesTo graph node
                            |> List.map (Inner >> extend graph state)
                            |> (fun addedStates -> List.append addedStates states')
                        step states'' results
                | _ ->
                    step states' results
        let states = [ { Path = Target targetNode; Condition = condFn.GetEmpty(); Versions = Map.empty; Heap = heapFn.GetEmpty() } ]
        step states []
        
    // Systematically merge program paths (heap is currently not supported)

    let mergeRun condFn graph (targetNode:Node) =
        let getNodeCondVar id =
            let name = sprintf "node!!%d" <| NodeId.Value id
            Var { Name = name; Sort = Bool }

        let nodeCount = List.length graph.Nodes
        let relevant = Array.create nodeCount false
        let deps = Array.create nodeCount List.empty<NodeId>
        let depsClosure = Array.create nodeCount Set.empty<NodeId>
        let enterNode = Graph.enterNode graph

        let markRelevant () (id:NodeId) =
            relevant.[id.Value] <- true
            ((), true)
        Graph.dfs (Graph.backwardExtender graph) markRelevant () graph targetNode.Id

        let relevantExtender = Graph.forwardExtender graph >> List.filter (fun id -> relevant.[id.Value])
        let rec processDependency id =
            let getNestedDeps nextId =
                match depsClosure.[NodeId.Value id] with
                | nextDeps when nextDeps.IsEmpty && id <> targetNode.Id ->
                    processDependency nextId
                | nextDeps ->
                    nextDeps

            let currentDeps = relevantExtender id
            let nestedDeps =
                currentDeps
                |> List.map getNestedDeps
                |> Utils.cons (Set.ofList currentDeps)
                |> Set.unionMany
            deps.[id.Value] <- currentDeps
            depsClosure.[id.Value] <- nestedDeps
            nestedDeps
        processDependency enterNode.Id |> ignore

        let processed = Array.create nodeCount false
        processed.[targetNode.Id.Value] <- true
        let asserts = Array.create nodeCount <| BoolConst true
        let conditions = Array.create nodeCount <| condFn.GetEmpty()
        let versions = Array.create nodeCount Map.empty<string, int>
        let varSorts = Array.create nodeCount Map.empty<string, Sort>

        let rec processCondition id =
            let nextIds = deps.[NodeId.Value id]
            for nextId in nextIds do
                match processed.[nextId.Value] with
                | false -> processCondition nextId
                | true -> ()

            let edges =
                Graph.edgesFromId graph id
                |> List.filter (fun edge -> List.contains edge.To nextIds)
            
            let (currentAssert, finalVersions) =
                let mergedVersions =
                    nextIds
                    |> List.map (NodeId.Value >> Array.get versions)
                    |> List.fold mergeVersions Map.empty
                let getJoinCond (edge:InnerEdge) =
                    let nextVersions = versions.[edge.To.Value]
                    let nextVariables = varSorts.[edge.To.Value]
                    let edgeCond = addVersions nextVersions edge.Condition
                    let versionMergeCond = 
                        let addVarMerge term name sort =
                            let nextVersion = getVersion nextVersions name
                            match getVersion mergedVersions name with
                            | mergedVersion when mergedVersion > nextVersion ->
                                let oldVar = Var { Name = formatVersioned name nextVersion; Sort = sort }
                                let newVar = Var { Name = formatVersioned name mergedVersion; Sort = sort }
                                Term.foldAnd term <| Eq (oldVar, newVar)
                            | _ ->
                                term
                        Map.fold addVarMerge (BoolConst true) nextVariables
                    Term.foldAnd edgeCond versionMergeCond
                    |> Term.foldAnd <| getNodeCondVar edge.To

                let joinDisjunction = edges |> Seq.map getJoinCond |> Term.disjunction
                let node = Graph.node graph id
                let (operationCondOpt, finalVersions, _) = processNode unsupportedHeapFn node mergedVersions ()
                let operationCond = Option.defaultValue (BoolConst true) operationCondOpt
                let currentAssert = Implies (getNodeCondVar id, Term.foldAnd joinDisjunction operationCond)
                (currentAssert, finalVersions)

            let variableSorts =
                let addTermVariable varSorts term =
                    match term with
                    | Var v -> Map.add v.Name v.Sort varSorts
                    | _ -> varSorts
                let getTermVariables term =
                    Term.fold addTermVariable Map.empty term
                let addOperationVariables varSorts op =
                    let operationVars =
                        match op with
                        | Assign assign ->
                            getTermVariables assign.Value
                            |> Map.add assign.Target.Name assign.Target.Sort
                        | HeapOp heapOp ->
                            let trgVar =
                                HeapOperation.targetVariable heapOp
                                |> Option.map (fun variable -> Map.add variable.Name variable.Sort Map.empty)
                            let term =
                                HeapOperation.term heapOp
                                |> Option.map getTermVariables
                            Utils.mergeOptions Utils.mergeMaps trgVar term
                            |> Option.defaultValue Map.empty
                    Utils.mergeMaps varSorts operationVars

                let edgeVariables =
                    edges
                    |> List.map (InnerEdge.Condition >> getTermVariables)
                    |> List.fold Utils.mergeMaps Map.empty
                let edgeAndNodeVariables =
                    Node.operations <| Graph.node graph id
                    |> List.fold addOperationVariables edgeVariables
                nextIds
                |> List.map (NodeId.Value >> Array.get varSorts)
                |> List.fold Utils.mergeMaps edgeAndNodeVariables

            let pathCond =
                match nextIds with
                | [ onlyId ] ->
                    assertCondition condFn conditions.[onlyId.Value] currentAssert
                | (firstId :: otherIds) ->
                    let currentNodes = Set.add firstId depsClosure.[firstId.Value]
                    let addedAsserts =
                        otherIds
                        |> List.map (fun id -> depsClosure.[id.Value])
                        |> Utils.cons (Set.ofList otherIds)
                        |> Set.unionMany
                        |> Utils.swap Set.difference currentNodes
                        |> List.ofSeq
                        |> List.map (NodeId.Value >> Array.get asserts)
                        |> Utils.cons currentAssert
                    let baseCond = conditions.[firstId.Value]
                    List.fold (assertCondition condFn) baseCond addedAsserts
                | [] ->
                    // No dependencies are only from the target node, which is marked as completed by default
                    failwith "Unreachable"

            asserts.[id.Value] <- currentAssert
            conditions.[id.Value] <- pathCond
            versions.[id.Value] <- finalVersions
            varSorts.[id.Value] <- variableSorts
            processed.[id.Value] <- true

        processCondition enterNode.Id

        let cond = condFn.Assert (getNodeCondVar enterNode.Id) conditions.[enterNode.Id.Value]
        let res = condFn.Solve cond

        // TODO: Remove once completed
        let termTexts = Array.map Term.print asserts

        // TODO: Produce paths according to the model
        ()
