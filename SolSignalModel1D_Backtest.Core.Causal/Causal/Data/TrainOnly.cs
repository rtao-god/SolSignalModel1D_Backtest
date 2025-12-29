using System.Collections;
using System.Globalization;
using SolSignalModel1D_Backtest.Core.Causal.Causal.Time;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
	{
	/// <summary>
	/// Маркерный контейнер: "это гарантированно train-only набор по контракту TrainBoundary".
	/// Создать экземпляр извне нельзя: только TrainBoundary выдаёт TrainOnly после строгого сплита.
	/// </summary>
	public sealed class TrainOnly<T> : IReadOnlyList<T>
		{
		private readonly T[] _items;

		public TrainUntilExitDayKeyUtc TrainUntilExitDayKeyUtc { get; }
		public string Tag { get; }

		internal TrainOnly ( IReadOnlyList<T> items, TrainUntilExitDayKeyUtc trainUntilExitDayKeyUtc, string tag )
			{
			if (items == null) throw new ArgumentNullException (nameof (items));
			if (trainUntilExitDayKeyUtc.IsDefault)
				throw new ArgumentException ("trainUntilExitDayKeyUtc must be initialized (non-default).", nameof (trainUntilExitDayKeyUtc));

			Tag = string.IsNullOrWhiteSpace (tag)
				? throw new ArgumentException ("tag must be non-empty.", nameof (tag))
				: tag;

			TrainUntilExitDayKeyUtc = trainUntilExitDayKeyUtc;

			// Заморозка набора: исключаем случайные добавления/удаления и "as List<T>" трюки.
			_items = items.Count == 0 ? Array.Empty<T> () : items.ToArray ();
			}

		public int Count => _items.Length;

		public T this[int index] => _items[index];

		public IEnumerator<T> GetEnumerator () => ((IEnumerable<T>) _items).GetEnumerator ();

		IEnumerator IEnumerable.GetEnumerator () => _items.GetEnumerator ();

		public override string ToString () =>
			$"TrainOnly<{typeof (T).Name}>({Count} items, trainUntilExitDayKey={TrainUntilExitDayKeyUtc.Value.ToString ("yyyy-MM-dd", CultureInfo.InvariantCulture)}, tag='{Tag}')";
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
