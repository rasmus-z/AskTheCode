﻿using AskTheCode.ControlFlowGraphs.TypeSystem;
using AskTheCode.SmtLibStandard;
using Microsoft.CodeAnalysis;

namespace AskTheCode.ControlFlowGraphs.Cli.TypeSystem
{
    public class FieldDefinition : IFieldDefinition
    {
        private int? orderNumber;

        internal FieldDefinition(IFieldSymbol symbol, Sort sort, int? orderNumber = null)
        {
            this.Symbol = symbol;
            this.Sort = sort;
            this.orderNumber = orderNumber;
        }

        internal FieldDefinition(IFieldSymbol symbol, IClassDefinition referencedClass)
        {
            this.Symbol = symbol;
            this.Sort = CustomSorts.Reference;
            this.ReferencedClass = referencedClass;
        }

        public Sort Sort { get; }

        public IClassDefinition ReferencedClass { get; }

        internal IFieldSymbol Symbol { get; }


    }
}