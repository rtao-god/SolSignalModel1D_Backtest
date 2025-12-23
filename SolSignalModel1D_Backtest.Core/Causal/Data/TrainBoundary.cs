using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
{
    public sealed class TrainBoundary
    {
        private readonly BaselineExitUtc _trainUntil;

        public TrainBoundary(BaselineExitUtc trainUntil)
        {
            _trainUntil = trainUntil;
        }

        public TrainOosSplitStrict<T> SplitStrict<T>(IReadOnlyList<T> items, string tag)
            where T : IHasCausalStamp
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (string.IsNullOrWhiteSpace(tag))
                throw new ArgumentException("tag must be non-empty.", nameof(tag));

            var train = new List<T>(items.Count);
            var oos = new List<T>();

            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var exitUtc = it.Stamp.BaselineExitUtc;

                if (exitUtc.Value <= _trainUntil.Value)
                    train.Add(it);
                else
                    oos.Add(it);
            }

            return new TrainOosSplitStrict<T>(new TrainOnly<T>(train, _trainUntil, tag), oos);
        }
    }

    public sealed class TrainOnly<T>
    {
        public IReadOnlyList<T> Items { get; }
        public BaselineExitUtc TrainUntil { get; }
        public string Tag { get; }

        public int Count => Items.Count;

        public TrainOnly(IReadOnlyList<T> items, BaselineExitUtc trainUntil, string tag)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
            TrainUntil = trainUntil;
            Tag = tag;
        }
    }

    public readonly record struct TrainOosSplitStrict<T>(TrainOnly<T> Train, IReadOnlyList<T> Oos);
}
