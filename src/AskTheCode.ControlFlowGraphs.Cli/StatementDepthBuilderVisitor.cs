﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using AskTheCode.ControlFlowGraphs.Cli.TypeModels;
using AskTheCode.SmtLibStandard;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AskTheCode.ControlFlowGraphs.Cli
{
    internal class StatementDepthBuilderVisitor : BuilderVisitor
    {
        public StatementDepthBuilderVisitor(CSharpGraphBuilder.BuildingContext context)
            : base(context)
        {
        }

        public sealed override void VisitMethodDeclaration(MethodDeclarationSyntax methodSyntax)
        {
            Contract.Requires(this.Context.CurrentNode.OutgoingEdges.Count == 0);

            var enter = this.Context.ReenqueueCurrentNode(methodSyntax.ParameterList);
            var body = this.Context.EnqueueNode(methodSyntax.Body);
            enter.AddEdge(body);

            foreach (var parameterSyntax in methodSyntax.ParameterList.Parameters)
            {
                this.Context.TryGetModel(parameterSyntax);
            }

            if ((methodSyntax.ReturnType as PredefinedTypeSyntax)?.Keyword.Text == "void")
            {
                var implicitReturn = this.Context.AddFinalNode(methodSyntax.Body.CloseBraceToken);
                implicitReturn.BorderData = new BorderData(BorderDataKind.Return, null, null);
                body.AddEdge(implicitReturn);

                // TODO: Add also ReturnFlowNode here
            }

            return;
        }

        public sealed override void VisitReturnStatement(ReturnStatementSyntax returnSyntax)
        {
            this.Context.CurrentNode.OutgoingEdges.Clear();
            this.Context.CurrentNode.BorderData = new BorderData(BorderDataKind.Return, null, null);

            if (returnSyntax.Expression != null)
            {
                this.Context.ReenqueueCurrentNode(returnSyntax.Expression);
                this.Context.CurrentNode.LabelOverride = returnSyntax;
            }
        }

        // TODO: Handle also exception constructors with arguments
        public override void VisitThrowStatement(ThrowStatementSyntax throwSyntax)
        {
            this.Context.CurrentNode.OutgoingEdges.Clear();

            var constructorSyntax = throwSyntax.Expression as ObjectCreationExpressionSyntax;
            if (constructorSyntax != null && constructorSyntax.ArgumentList.Arguments.Count == 0)
            {
                var constructorSymbol =
                    this.Context.SemanticModel.GetSymbolInfo(constructorSyntax).Symbol as IMethodSymbol;
                Contract.Assert(constructorSymbol != null);

                this.Context.CurrentNode.BorderData = new BorderData(
                    BorderDataKind.ExceptionThrow,
                    constructorSymbol,
                    Enumerable.Empty<ITypeModel>());
            }
        }

        public sealed override void VisitBlock(BlockSyntax blockSyntax)
        {
            // TODO: Consider merging with the following node in the case of empty block
            //       (or leave it to the FlowGraph construction)
            this.ProcessSequentially(blockSyntax.Statements);

            return;
        }

        public sealed override void VisitIfStatement(IfStatementSyntax ifSyntax)
        {
            var outEdge = this.Context.CurrentNode.OutgoingEdges.SingleOrDefault();
            Contract.Assert(outEdge?.ValueCondition == null);

            this.Context.CurrentNode.OutgoingEdges.Clear();
            var condition = this.Context.ReenqueueCurrentNode(ifSyntax.Condition);
            var statement = this.Context.EnqueueNode(ifSyntax.Statement);
            condition.AddEdge(statement, ExpressionFactory.True);

            if (outEdge != null)
            {
                statement.AddEdge(outEdge);
            }

            if (ifSyntax.Else != null)
            {
                var elseBody = this.Context.EnqueueNode(ifSyntax.Else);
                condition.AddEdge(elseBody, ExpressionFactory.False);

                if (outEdge != null)
                {
                    elseBody.AddEdge(outEdge);
                }
            }
            else
            {
                if (outEdge == null)
                {
                    // TODO: Add a message and put to resources
                    //       (probably related to: "Not all code paths return a value")
                    throw new InvalidOperationException();
                }

                condition.AddEdge(outEdge.To, ExpressionFactory.False);
            }

            // TODO: Handle in a more sophisticated way, not causing "if (boolVar)" to create a helper variable
            if (condition.VariableModel == null)
            {
                condition.VariableModel = this.Context.TryCreateTemporaryVariableModel(ifSyntax.Condition);
            }

            return;
        }

        public sealed override void VisitElseClause(ElseClauseSyntax elseSyntax)
        {
            this.Context.CurrentNode.Syntax = elseSyntax.Statement;
            this.Visit(elseSyntax.Statement);
        }

        public sealed override void VisitWhileStatement(WhileStatementSyntax whileSyntax)
        {
            var outEdge = this.Context.CurrentNode.GetSingleEdge();

            this.Context.CurrentNode.OutgoingEdges.Clear();
            var condition = this.Context.ReenqueueCurrentNode(whileSyntax.Condition);
            var statement = this.Context.EnqueueNode(whileSyntax.Statement);
            condition.AddEdge(statement, ExpressionFactory.True);
            statement.AddEdge(condition);

            this.Context.CurrentNode.OutgoingEdges.Add(outEdge.WithValueCondition(ExpressionFactory.False));

            // TODO: Handle in a more sophisticated way, not causing "while (boolVar)" to create a helper variable
            if (condition.VariableModel == null)
            {
                condition.VariableModel = this.Context.TryCreateTemporaryVariableModel(whileSyntax.Condition);
            }

            return;
        }

        protected void ProcessSequentially<TSyntax>(IReadOnlyList<TSyntax> syntaxes)
            where TSyntax : SyntaxNode
        {
            if (syntaxes.Count > 0)
            {
                var precedingStatement = this.Context.ReenqueueCurrentNode(syntaxes.First());

                if (syntaxes.Count > 1)
                {
                    var outEdges = precedingStatement.OutgoingEdges.ToArray();
                    precedingStatement.OutgoingEdges.Clear();

                    for (int i = 1; i < syntaxes.Count; i++)
                    {
                        var currentStatement = this.Context.EnqueueNode(syntaxes[i]);
                        precedingStatement.AddEdge(currentStatement);

                        precedingStatement = currentStatement;
                    }

                    precedingStatement.OutgoingEdges.AddRange(outEdges);
                }
            }
        }
    }
}
