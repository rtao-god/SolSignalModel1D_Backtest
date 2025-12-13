using System;
using System.Collections.Generic;
using SolSignalModel1D_Backtest.Core.Domain;

namespace SolSignalModel1D_Backtest.Core.Analytics.Sampling
	{
	/// <summary>
	/// Сэмплинг/группировки рядов для аналитики.
	/// ВАЖНО: эта зона не является time-contract, но она обязана быть детерминированной и строгой:
	/// - входной ряд должен быть отсортирован по DateUtc;
	/// - ошибки входа должны выявляться исключением, а не «молчаливой сортировкой».
	/// </summary>
	public static class DataRowSampling
		{
		/// <summary>
		/// Строит spaced-test: берёт несколько блоков из конца ряда с пропусками между блоками.
		/// Предусловие: rows отсортирован по DateUtc по возрастанию.
		/// </summary>
		public static List<T> BuildSpacedTest<T> ( IReadOnlyList<T> rows, int take, int skip, int blocks )
			where T : IHasDateUtc
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (take <= 0) throw new ArgumentOutOfRangeException (nameof (take), "take must be > 0.");
			if (blocks <= 0) throw new ArgumentOutOfRangeException (nameof (blocks), "blocks must be > 0.");
			if (skip < 0) throw new ArgumentOutOfRangeException (nameof (skip), "skip must be >= 0.");

			if (rows.Count == 0)
				return new List<T> ();

			EnsureSortedByDateUtc (rows);

			var ranges = new List<(int Start, int EndExclusive)> (capacity: blocks);
			var endExclusive = rows.Count;

			for (int b = 0; b < blocks && endExclusive > 0; b++)
				{
				var start = endExclusive - take;
				if (start < 0) start = 0;

				ranges.Add ((start, endExclusive));

				// следующий блок — раньше, с пропуском
				endExclusive = start - skip;
				}

			// Делаем порядок строго хронологическим без финальной сортировки O(n log n).
			ranges.Reverse ();

			var total = 0;
			foreach (var r in ranges) total += (r.EndExclusive - r.Start);

			var res = new List<T> (capacity: total);
			foreach (var (start, end) in ranges)
				{
				for (int i = start; i < end; i++)
					res.Add (rows[i]);
				}

			return res;
			}

		/// <summary>
		/// Группирует строки на блоки фиксированного размера в исходном (хронологическом) порядке.
		/// Предусловие: rows отсортирован по DateUtc по возрастанию.
		/// </summary>
		public static IEnumerable<List<T>> GroupByBlocks<T> ( IReadOnlyList<T> rows, int blockSize )
			where T : IHasDateUtc
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (blockSize <= 0) throw new ArgumentOutOfRangeException (nameof (blockSize));

			if (rows.Count == 0)
				yield break;

			EnsureSortedByDateUtc (rows);

			var cur = new List<T> (capacity: blockSize);
			for (int i = 0; i < rows.Count; i++)
				{
				cur.Add (rows[i]);
				if (cur.Count == blockSize)
					{
					yield return cur;
					cur = new List<T> (capacity: blockSize);
					}
				}

			if (cur.Count > 0)
				yield return cur;
			}

		private static void EnsureSortedByDateUtc<T> ( IReadOnlyList<T> rows )
			where T : IHasDateUtc
			{
			for (int i = 1; i < rows.Count; i++)
				{
				var prev = rows[i - 1].DateUtc;
				var cur = rows[i].DateUtc;

				if (cur < prev)
					{
					throw new InvalidOperationException (
						$"Rows must be sorted by DateUtc ascending. " +
						$"Found inversion at i={i}: prev={prev:O}, cur={cur:O}.");
					}
				}
			}
		}
	}
