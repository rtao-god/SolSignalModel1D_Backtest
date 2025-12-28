using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.Causal.Utils.Collections
	{
	/// <summary>
	/// Коллекционные утилиты для "оконных" сэмплов и блочного разбиения.
	/// Держим отдельно от time-contract, чтобы не смешивать слои.
	/// </summary>
	public static class BlockSampling
		{
		/// <summary>
		/// Берёт блоки с конца: (take), затем пропускает (skip), и так blocks раз.
		/// Возвращает в хронологическом порядке.
		/// </summary>
		public static List<T> BuildSpacedTest<T> (
			IReadOnlyList<T> rows,
			int take,
			int skip,
			int blocks,
			Func<T, DateTime> dateSelector )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (dateSelector == null) throw new ArgumentNullException (nameof (dateSelector));
			if (take <= 0) throw new ArgumentOutOfRangeException (nameof (take));
			if (skip < 0) throw new ArgumentOutOfRangeException (nameof (skip));
			if (blocks <= 0) throw new ArgumentOutOfRangeException (nameof (blocks));

			var picked = new List<T> (Math.Min (rows.Count, take * blocks));

			int idx = rows.Count; // exclusive
			for (int b = 0; b < blocks && idx > 0; b++)
				{
				int takeStart = Math.Max (0, idx - take);
				for (int i = takeStart; i < idx; i++)
					picked.Add (rows[i]);

				idx = takeStart - skip;
				}

			// Инвариант: возвращаем в хронологическом порядке.
			picked.Sort (( a, b ) => dateSelector (a).CompareTo (dateSelector (b)));
			return picked;
			}

		/// <summary>
		/// Разбивает на последовательные блоки фиксированного размера.
		/// </summary>
		public static IEnumerable<List<T>> GroupByBlocks<T> ( IReadOnlyList<T> rows, int blockSize )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (blockSize <= 0) throw new ArgumentOutOfRangeException (nameof (blockSize));

			var cur = new List<T> (blockSize);

			for (int i = 0; i < rows.Count; i++)
				{
				cur.Add (rows[i]);

				if (cur.Count == blockSize)
					{
					yield return cur;
					cur = new List<T> (blockSize);
					}
				}

			if (cur.Count > 0)
				yield return cur;
			}
		}
	}
