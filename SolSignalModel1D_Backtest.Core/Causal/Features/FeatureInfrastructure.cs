using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Features
{
    // Invariant: TRow is part of FeatureContext public surface (Row getter),
    // therefore variance is not allowed here.
    public interface IFeatureBuilder<TRow>
    {
        void Build(FeatureContext<TRow> ctx);
    }

    public sealed class FeatureContext<TRow>
    {
        public TRow Row { get; }
        public CausalStamp Stamp { get; }
        public List<double> Features { get; }

        public FeatureContext(TRow row, CausalStamp stamp, int capacity = 64)
        {
            if (row is null) throw new ArgumentNullException(nameof(row));
            Row = row;
            Stamp = stamp;
            Features = new List<double>(capacity);
        }

        public void Add(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                throw new InvalidOperationException($"[features] non-finite value at entry={Stamp.EntryUtc}: {v}");

            Features.Add(v);
        }

        public void Add(double? v, string name)
        {
            if (v is null)
                throw new InvalidOperationException($"[features] missing '{name}' at entry={Stamp.EntryUtc}");

            Add(v.Value);
        }

        public void Add01(bool v) => Add(v ? 1.0 : 0.0);

        public void Add01(bool? v, string name)
        {
            if (v is null)
                throw new InvalidOperationException($"[features] missing '{name}' at entry={Stamp.EntryUtc}");

            Add01(v.Value);
        }
    }

    public sealed class FeaturePipeline<TRow>
    {
        private readonly IFeatureBuilder<TRow>[] _builders;

        public FeaturePipeline(params IFeatureBuilder<TRow>[] builders)
        {
            _builders = builders ?? Array.Empty<IFeatureBuilder<TRow>>();

            for (int i = 0; i < _builders.Length; i++)
            {
                if (_builders[i] is null)
                    throw new ArgumentNullException(nameof(builders), "builders contains null item.");
            }
        }

        public List<double> Run(TRow row, CausalStamp stamp)
        {
            var ctx = new FeatureContext<TRow>(row, stamp);

            for (int i = 0; i < _builders.Length; i++)
                _builders[i].Build(ctx);

            return ctx.Features;
        }
    }
}
