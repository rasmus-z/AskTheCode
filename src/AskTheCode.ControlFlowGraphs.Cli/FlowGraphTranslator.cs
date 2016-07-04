﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AskTheCode.Common;
using AskTheCode.ControlFlowGraphs.Cli.TypeModels;
using AskTheCode.SmtLibStandard;
using AskTheCode.SmtLibStandard.Handles;

namespace AskTheCode.ControlFlowGraphs.Cli
{
    using IngoingEdgesOverlay = OrdinalOverlay<BuildNodeId, BuildNode, List<FlowGraphTranslator.EdgeInfo>>;

    internal class FlowGraphTranslator
    {
        private FlowGraphId flowGraphId;
        private FlowGraphBuilder builder;
        private IngoingEdgesOverlay ingoingEdges;
        private ExpressionTranslator expressionTranslator;
        private OrdinalOverlay<BuildVariableId, BuildVariable, FlowVariable> buildToFlowVariablesMap;

        public FlowGraphTranslator(BuildGraph buildGraph, FlowGraphId flowGraphId)
        {
            this.BuildGraph = buildGraph;
            this.flowGraphId = flowGraphId;
        }

        public BuildGraph BuildGraph { get; private set; }

        public FlowGraph FlowGraph { get; private set; }

        // TODO: Consider splitting into multiple methods to increase readability
        public FlowGraph Translate()
        {
            this.builder = new FlowGraphBuilder(this.flowGraphId);
            this.ingoingEdges = this.ComputeIngoingEdges(this.BuildGraph);

            this.expressionTranslator = new ExpressionTranslator(this);
            this.buildToFlowVariablesMap = new OrdinalOverlay<BuildVariableId, BuildVariable, FlowVariable>();
            var buildToFlowNodesMap = new OrdinalOverlay<BuildNodeId, BuildNode, FlowNode>();
            var nodeQueue = new Queue<BuildNode>();
            var edgeQueue = new Queue<EdgeInfo>();
            var visitedNodes = new OrdinalOverlay<BuildNodeId, BuildNode, bool>();

            var buildParameters = this.BuildGraph.Variables.Where(variable => variable.Origin == VariableOrigin.Parameter);
            var flowParameters = new List<FlowVariable>();
            foreach (var parameter in buildParameters)
            {
                flowParameters.Add(this.TranslateVariable(parameter));
            }

            var flowEnterNode = this.builder.AddEnterNode(flowParameters);
            buildToFlowNodesMap[this.BuildGraph.EnterNode] = flowEnterNode;

            Contract.Assert(this.BuildGraph.EnterNode.OutgoingEdges.Count == 1);
            var firstNonEnterNode = this.BuildGraph.EnterNode.OutgoingEdges.Single().To;
            nodeQueue.Enqueue(firstNonEnterNode);
            visitedNodes[firstNonEnterNode] = true;

            while (nodeQueue.Count > 0)
            {
                var buildNode = nodeQueue.Dequeue();
                Contract.Assert(visitedNodes[buildNode]);
                Contract.Assert(buildToFlowNodesMap[buildNode] == null);

                BuildNode firstBuildNode, lastBuildNode;

                var flowNode = this.TryTranslateBorderNode(buildNode);
                if (flowNode != null)
                {
                    firstBuildNode = buildNode;
                    lastBuildNode = buildNode;

                    buildToFlowNodesMap[buildNode] = flowNode;
                }
                else
                {
                    List<Assignment> assignments;

                    this.ProcessInnerNodesSequence(buildNode, out firstBuildNode, out lastBuildNode, out assignments);

                    flowNode = this.builder.AddInnerNode(assignments);
                    foreach (var processedNode in this.GetBuildNodesSequenceRange(firstBuildNode, lastBuildNode))
                    {
                        buildToFlowNodesMap[processedNode] = flowNode;
                    }
                }

                // TODO: Try to get rid of the empty nodes (empty blocks etc.)
                Contract.Assert(flowNode != null);

                foreach (var edge in lastBuildNode.OutgoingEdges)
                {
                    if (buildToFlowNodesMap[edge.To] == null)
                    {
                        nodeQueue.Enqueue(edge.To);
                        visitedNodes[edge.To] = true;
                    }
                }

                foreach (var edgeInfo in this.ingoingEdges[firstBuildNode])
                {
                    var flowFromNode = buildToFlowNodesMap[edgeInfo.From];
                    if (flowFromNode == null)
                    {
                        edgeQueue.Enqueue(edgeInfo);
                    }
                    else
                    {
                        this.TranslateEdge(edgeInfo.Edge, edgeInfo.From, flowFromNode, flowNode);
                    }
                }
            }

            while (edgeQueue.Count > 0)
            {
                var edgeInfo = edgeQueue.Dequeue();
                FlowNode flowFrom = buildToFlowNodesMap[edgeInfo.From];
                FlowNode flowTo = buildToFlowNodesMap[edgeInfo.Edge.To];

                Contract.Assert(flowFrom != null);
                Contract.Assert(flowTo != null);
                this.TranslateEdge(edgeInfo.Edge, edgeInfo.From, flowFrom, flowTo);
            }

            this.FlowGraph = this.builder.FreezeAndReleaseGraph();
            return this.FlowGraph;
        }

        private IngoingEdgesOverlay ComputeIngoingEdges(BuildGraph graph)
        {
            var ingoing = new IngoingEdgesOverlay(() => new List<EdgeInfo>());

            var visited = new OrdinalOverlay<BuildNodeId, BuildNode, bool>();

            var stack = new Stack<BuildNode>();
            stack.Push(graph.EnterNode);
            visited[graph.EnterNode] = true;

            while (stack.Count > 0)
            {
                var node = stack.Pop();

                foreach (var edge in node.OutgoingEdges)
                {
                    var edgeInfo = new EdgeInfo(edge, node);
                    ingoing[edge.To].Add(edgeInfo);

                    if (!visited[edge.To])
                    {
                        stack.Push(edge.To);
                        visited[edge.To] = true;
                    }
                }
            }

            return ingoing;
        }

        private FlowNode TryTranslateBorderNode(BuildNode buildNode)
        {
            var borderData = buildNode.BorderData;
            if (borderData == null)
            {
                return null;
            }

            if (borderData.Kind == BorderDataKind.MethodCall || borderData.Kind == BorderDataKind.ExceptionThrow)
            {
                var location = new MethodLocation(borderData.Method);
                var buildArguments = borderData.Arguments.SelectMany(typeModel => typeModel.AssignmentRight);
                var flowArguments = buildArguments.Select(expression => this.TranslateExpression(expression));

                if (borderData.Kind == BorderDataKind.MethodCall)
                {
                    var returnAssignments = buildNode.VariableModel?.AssignmentLeft
                        .Select(buildVar => this.TranslateVariable((BuildVariable)buildVar));

                    return this.builder.AddCallNode(location, flowArguments, returnAssignments);
                }
                else
                {
                    Contract.Assert(borderData.Kind == BorderDataKind.ExceptionThrow);

                    return this.builder.AddThrowExceptionNode(location, flowArguments);
                }
            }
            else
            {
                Contract.Assert(borderData.Kind == BorderDataKind.Return);

                var returnValues = buildNode.ValueModel?.AssignmentRight
                    .Select(expression => this.TranslateExpression(expression));

                return this.builder.AddReturnNode(returnValues);
            }
        }

        // TODO: Substitute the temporary variables only used once in the sequence to reduce the number of assignments
        private void ProcessInnerNodesSequence(
            BuildNode buildNode,
            out BuildNode firstBuildNode,
            out BuildNode lastBuildNode,
            out List<Assignment> assignments)
        {
            assignments = new List<Assignment>();
            var curNode = buildNode;
            firstBuildNode = curNode;

            while (true)
            {
                var nodeAssignments = this.TranslateAssignments(curNode.VariableModel, curNode.ValueModel);
                assignments.AddRange(nodeAssignments);

                if (curNode.OutgoingEdges.Count > 1)
                {
                    break;
                }

                Contract.Assert(curNode.OutgoingEdges.Count == 1);
                var nextNode = curNode.OutgoingEdges.Single().To;

                if (nextNode.BorderData != null || this.ingoingEdges[nextNode].Count > 1)
                {
                    break;
                }

                Contract.Assert(this.ingoingEdges[nextNode].Single().From == curNode);
                curNode = nextNode;
            }

            lastBuildNode = curNode;
        }

        private IEnumerable<Assignment> TranslateAssignments(ITypeModel variableModel, ITypeModel valueModel)
        {
            if (variableModel == null || valueModel == null || variableModel.AssignmentLeft.Count == 0)
            {
                Contract.Assert(valueModel == null || valueModel.AssignmentRight.Count == 0);

                yield break;
            }

            var variables = variableModel.AssignmentLeft;
            var values = valueModel.AssignmentRight;
            Contract.Assert(variables.Count == values.Count);

            for (int i = 0; i < variables.Count; i++)
            {
                Contract.Assert(variables[i].Sort == values[i].Sort);

                var translatedVariable = this.TranslateVariable((BuildVariable)variables[i]);
                var translatedValue = this.TranslateExpression(values[i]);
                Contract.Assert(translatedVariable.Sort == variables[i].Sort);
                Contract.Assert(translatedVariable.Sort == translatedValue.Sort);

                yield return new Assignment(translatedVariable, translatedValue);
            }
        }

        private FlowEdge TranslateEdge(
            BuildEdge buildEdge,
            BuildNode buildFrom,
            FlowNode flowFrom,
            FlowNode flowTo)
        {
            BoolHandle condition;
            if (buildEdge.ValueCondition?.Sort == Sort.Bool)
            {
                Contract.Assert(buildFrom.VariableModel != null);
                Contract.Assert(buildFrom.VariableModel is BooleanModel);

                var variable = buildFrom.VariableModel.AssignmentLeft.Single();
                condition = (BoolHandle)this.TranslateVariable((BuildVariable)variable);
                if (buildEdge.ValueCondition == ExpressionFactory.False)
                {
                    condition = !condition;
                }
                else
                {
                    Contract.Assert(buildEdge.ValueCondition == ExpressionFactory.True);
                }
            }
            else if (buildEdge.ValueCondition != null)
            {
                Contract.Assert(buildFrom.VariableModel != null);

                var variables = buildFrom.VariableModel.AssignmentLeft
                    .Select(buildVar => this.TranslateVariable((BuildVariable)buildVar))
                    .ToArray();

                // TODO: Update when BuildEdge.ValueCondition is changed to IValueModel
                var values = buildEdge.ValueCondition.ToSingular()
                    .Select(expression => this.TranslateExpression(expression))
                    .ToArray();

                // TODO: Consider overriding the comparison by the type models (string might use that)
                Contract.Assert(variables.Length == values.Length);
                if (variables.Length == 1)
                {
                    condition = (BoolHandle)ExpressionFactory.Equal(variables.Single(), values.Single());
                }
                else
                {
                    Contract.Assert(variables.Length > 1);

                    var conditions = new List<Expression>();
                    for (int i = 0; i < variables.Length; i++)
                    {
                        conditions.Add(ExpressionFactory.Equal(variables[i], values[i]));
                    }

                    condition = (BoolHandle)ExpressionFactory.And(conditions.ToArray());
                }
            }
            else
            {
                condition = true;
            }

            return this.builder.AddEdge(flowFrom, flowTo, condition);
        }

        private FlowVariable TranslateVariable(BuildVariable buildVariable)
        {
            var flowVariable = this.buildToFlowVariablesMap[buildVariable];

            if (flowVariable == null)
            {
                flowVariable = this.builder.AddLocalVariable(buildVariable.Sort, buildVariable.Symbol?.Name);
                this.buildToFlowVariablesMap[buildVariable] = flowVariable;
            }

            return flowVariable;
        }

        private Expression TranslateExpression(Expression expression)
        {
            return this.expressionTranslator.Visit(expression);
        }

        private IEnumerable<BuildNode> GetBuildNodesSequenceRange(BuildNode first, BuildNode last)
        {
            for (var current = first; ; current = current.OutgoingEdges.Single().To)
            {
                yield return current;

                if (current == last)
                {
                    break;
                }
            }
        }

        internal class EdgeInfo
        {
            public EdgeInfo(BuildEdge edge, BuildNode from)
            {
                Contract.Requires(edge != null);
                Contract.Requires(from != null);
                Contract.Requires(from.OutgoingEdges.Contains(edge));

                this.Edge = edge;
                this.From = from;
            }

            public BuildEdge Edge { get; private set; }

            public BuildNode From { get; private set; }
        }

        private class ExpressionTranslator : ExpressionRewriter
        {
            private FlowGraphTranslator owner;

            public ExpressionTranslator(FlowGraphTranslator owner)
            {
                Contract.Requires(owner != null);

                this.owner = owner;
            }

            public override Expression VisitVariable(Variable variable)
            {
                Contract.Assert(variable is BuildVariable);

                return this.owner.TranslateVariable((BuildVariable)variable);
            }
        }
    }
}