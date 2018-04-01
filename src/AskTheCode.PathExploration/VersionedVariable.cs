﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AskTheCode.ControlFlowGraphs;

namespace AskTheCode.PathExploration
{
    public struct VersionedVariable : IEquatable<VersionedVariable>
    {
        public static readonly VersionedVariable Null = new VersionedVariable(References.Null, 0);

        public VersionedVariable(FlowVariable variable, int version)
        {
            this.Variable = variable;
            this.Version = version;
        }

        public FlowVariable Variable { get; }

        public int Version { get; }

        public bool Equals(VersionedVariable other)
        {
            return this.Variable == other.Variable && this.Version == other.Version;
        }

        public override bool Equals(object obj)
        {
            if (obj is VersionedVariable other)
            {
                return this.Equals(other);
            }
            else
            {
                return false;
            }
        }

        public override int GetHashCode()
        {
            return this.Variable.GetHashCode() ^ this.Version;
        }

        public override string ToString() => $"{this.Variable}#{this.Version}";
    }
}
