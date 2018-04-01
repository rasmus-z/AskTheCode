﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AskTheCode.ControlFlowGraphs;
using AskTheCode.PathExploration.Heap;
using AskTheCode.SmtLibStandard;
using AskTheCode.SmtLibStandard.Handles;
using CodeContractsRevival.Runtime;

namespace AskTheCode.PathExploration
{
    internal class PathConditionHandler : PathVariableVersionHandler
    {
        private readonly SmtContextHandler contextHandler;
        private readonly ISolver smtSolver;
        private readonly VersionedNameProvider nameProvider;

        public PathConditionHandler(
            SmtContextHandler contextHandler,
            ISolver smtSolver,
            Path path,
            StartingNodeInfo startingNode,
            ISymbolicHeap heap)
            : base(path, startingNode, heap)
        {
            Contract.Requires(contextHandler != null);
            Contract.Requires(smtSolver != null);
            Contract.Requires(path != null);
            Contract.Requires(startingNode != null);
            Contract.Requires(heap != null);

            this.contextHandler = contextHandler;
            this.smtSolver = smtSolver;

            this.nameProvider = new VersionedNameProvider(this);

            this.smtSolver.Push();

            this.ProcessStartingNode();
        }

        protected override void OnAfterPathRetracted(int popCount)
        {
            // It is done as batch for performance reasons
            if (popCount > 0)
            {
                this.smtSolver.Pop(popCount);
            }
        }

        protected override void OnBeforePathStepExtended()
        {
            this.smtSolver.Push();
        }

        protected override void OnConditionAsserted(BoolHandle condition)
        {
            this.smtSolver.AddAssertion(this.nameProvider, condition);
        }

        protected override void OnVariableAssigned(FlowVariable variable, int lastVersion, Expression value)
        {
            if (!variable.IsReference)
            {
                this.AssertEquals(variable, lastVersion, value);
            }
        }

        private void AssertEquals(FlowVariable variable, int version, Expression value)
        {
            var symbolName = this.contextHandler.GetVariableVersionSymbol(variable, version);
            var symbolWrapper = new ConcreteVariableSymbolWrapper(variable, symbolName);

            var equal = (BoolHandle)ExpressionFactory.Equal(symbolWrapper, value);
            this.smtSolver.AddAssertion(this.nameProvider, equal);
        }

        private class ConcreteVariableSymbolWrapper : Variable
        {
            public ConcreteVariableSymbolWrapper(FlowVariable variable, SymbolName symbolName)
                : base(variable.Sort)
            {
                Contract.Requires(variable != null);
                Contract.Requires(symbolName.IsValid);

                this.Variable = variable;
                this.SymbolName = symbolName;
            }

            public override string DisplayName
            {
                get { return this.SymbolName.ToString(); }
            }

            public FlowVariable Variable { get; private set; }

            public SymbolName SymbolName { get; private set; }
        }

        private class VersionedNameProvider : INameProvider<Variable>
        {
            private PathConditionHandler owner;

            public VersionedNameProvider(PathConditionHandler owner)
            {
                this.owner = owner;
            }

            public SymbolName GetName(Variable variable)
            {
                if (variable is FlowVariable flowVariable)
                {
                    int version = this.owner.GetVariableVersion(flowVariable);
                    return this.owner.contextHandler.GetVariableVersionSymbol(flowVariable, version);
                }
                else if (variable is ConcreteVariableSymbolWrapper symbolWrapper)
                {
                    return symbolWrapper.SymbolName;
                }
                //else if (variable is ReferenceComparisonVariable refComp)
                //{
                //    return this.owner.GetReferenceComparisonExpression(refComp);
                //}

                throw new InvalidOperationException();
            }
        }
    }
}
