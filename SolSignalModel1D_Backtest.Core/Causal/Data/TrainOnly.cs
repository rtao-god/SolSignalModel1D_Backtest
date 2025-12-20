using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
	{
	/// <summary>
	/// Маркерный контейнер: "это гарантированно train-only набор по контракту TrainBoundary".
	/// Создать экземпляр извне нельзя: только TrainBoundary выдаёт TrainOnly после строгого сплита.
	/// </summary>
	public sealed class TrainOnly<T> : IReadOnlyList<T>
		{
		private readonly T[] _items;

		public DateTime TrainUntilUtc { get; }
		public string Tag { get; }

		internal TrainOnly ( IReadOnlyList<T> items, DateTime trainUntilUtc, string tag )
			{
			if (items == null) throw new ArgumentNullException (nameof (items));
			if (trainUntilUtc == default)
				throw new ArgumentException ("trainUntilUtc must be initialized (non-default).", nameof (trainUntilUtc));
			if (trainUntilUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException ("trainUntilUtc must be UTC (DateTimeKind.Utc).", nameof (trainUntilUtc));

			Tag = string.IsNullOrWhiteSpace (tag)
				? throw new ArgumentException ("tag must be non-empty.", nameof (tag))
				: tag;

			TrainUntilUtc = trainUntilUtc;

			// Заморозка набора: исключаем случайные добавления/удаления и "as List<T>" трюки.
			_items = items.Count == 0 ? Array.Empty<T> () : items.ToArray ();
			}

		public int Count => _items.Length;

		public T this[int index] => _items[index];

		public IEnumerator<T> GetEnumerator () => ((IEnumerable<T>) _items).GetEnumerator ();

		IEnumerator IEnumerable.GetEnumerator () => _items.GetEnumerator ();

		public override string ToString () =>
			$"TrainOnly<{typeof (T).Name}>({Count} items, trainUntil={TrainUntilUtc.ToString ("O", CultureInfo.InvariantCulture)}, tag='{Tag}')";
		}

	/// <summary>
	/// Результат строгого разбиения: train отдаётся как TrainOnly, OOS — обычным списком.
	/// </summary>
	public sealed class TrainOosSplitStrict<T>
		{
		public TrainOnly<T> Train { get; }
		public IReadOnlyList<T> Oos { get; }

		public TrainOosSplitStrict ( TrainOnly<T> train, IReadOnlyList<T> oos )
			{
			Train = train ?? throw new ArgumentNullException (nameof (train));
			Oos = oos ?? throw new ArgumentNullException (nameof (oos));
			}
		}
	}
