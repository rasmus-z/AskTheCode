﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AskTheCode.SmtLibStandard;

namespace AskTheCode.ControlFlowGraphs.Heap
{
    /// <summary>
    /// Represents a heap and its changes during a program execution.
    /// </summary>
    public interface IHeapModel
    {
        int MaxVersion { get; }

        IEnumerable<HeapModelLocation> GetLocations(int version);

        IEnumerable<HeapModelReference> GetReferences(HeapModelLocation location);

        IEnumerable<HeapModelValue> GetValues(HeapModelLocation location);
    }

    /// <summary>
    /// Represents a certain version of location within a <see cref="IHeapModel"/>.
    /// </summary>
    public struct HeapModelLocation
    {
        public const int NullId = 0;

        public static readonly HeapModelLocation Null = new HeapModelLocation(NullId, 0);

        public HeapModelLocation(int id, int heapVersion)
        {
            this.Id = id;
            this.HeapVersion = heapVersion;
        }

        public int Id { get; }

        public int HeapVersion { get; }

        public bool IsNull => this.Id == NullId;

        public override string ToString() => this.IsNull ? "NULL" : $"[{this.Id}] #{this.HeapVersion}";
    }

    /// <summary>
    /// Represents a reference within a <see cref="IHeapModel"/>.
    /// </summary>
    public struct HeapModelReference
    {
        public HeapModelReference(IFieldDefinition field, int locationId)
        {
            this.Field = field;
            this.LocationId = locationId;
        }

        public IFieldDefinition Field { get; }

        public int LocationId { get; }
    }

    /// <summary>
    /// Represents a field value within a <see cref="IHeapModel"/>.
    /// </summary>
    public struct HeapModelValue
    {
        public HeapModelValue(IFieldDefinition field, Interpretation value)
        {
            this.Field = field;
            this.Value = value;
        }

        public IFieldDefinition Field { get; }

        public Interpretation Value { get; }
    }
}
