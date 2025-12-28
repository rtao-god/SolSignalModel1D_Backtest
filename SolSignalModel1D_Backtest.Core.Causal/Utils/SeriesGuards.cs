namespace SolSignalModel1D_Backtest.Core.Causal.Utils
	{
	/// <summary>
	/// Инварианты временных рядов для каузального пайплайна:
	/// - все ключевые времена должны быть UTC;
	/// - порядок должен быть строго возрастающим (без дублей и разворотов).
	/// 
	/// Зачем:
	/// - убрать повторные OrderBy/ToList по всему коду;
	/// - ловить проблемы данных как можно раньше, а не "тихо чинить" внутри моделей.
	/// </summary>
	public static class SeriesGuards
		{
		public static void EnsureNonEmpty<T> ( IReadOnlyList<T> xs, string seriesName )
			{
			if (xs == null) throw new ArgumentNullException (nameof (xs));
			if (xs.Count == 0)
				throw new InvalidOperationException ($"[series] {seriesName}: empty series.");
			}

		public static void EnsureStrictlyAscendingUtc<T> (
			IReadOnlyList<T> xs,
			Func<T, DateTime> keyUtc,
			string seriesName )
			{
			if (xs == null) throw new ArgumentNullException (nameof (xs));
			if (keyUtc == null) throw new ArgumentNullException (nameof (keyUtc));

			if (xs.Count == 0)
				return;

			DateTime prev = keyUtc (xs[0]);
			if (prev.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ($"[series] {seriesName}: key[0] must be UTC, got Kind={prev.Kind}, t={prev:O}.");

			for (int i = 1; i < xs.Count; i++)
				{
				DateTime cur = keyUtc (xs[i]);

				if (cur.Kind != DateTimeKind.Utc)
					throw new InvalidOperationException ($"[series] {seriesName}: key[{i}] must be UTC, got Kind={cur.Kind}, t={cur:O}.");

				// Строго возрастающий порядок: исключаем дубли и развороты.
				if (cur <= prev)
					throw new InvalidOperationException (
						$"[series] {seriesName}: not strictly ascending at i={i}. prev={prev:O}, cur={cur:O}.");

				prev = cur;
				}
			}

		/// <summary>
		/// Единственная "разрешённая" сортировка: на бутстрапе/входе.
		/// Дальше по коду вместо OrderBy должны быть только EnsureStrictlyAscendingUtc.
		/// </summary>
		public static void SortByKeyUtcInPlace<T> (
			List<T> xs,
			Func<T, DateTime> keyUtc,
			string seriesName )
			{
			if (xs == null) throw new ArgumentNullException (nameof (xs));
			if (keyUtc == null) throw new ArgumentNullException (nameof (keyUtc));

			for (int i = 0; i < xs.Count; i++)
				{
				var t = keyUtc (xs[i]);
				if (t.Kind != DateTimeKind.Utc)
					throw new InvalidOperationException ($"[series] {seriesName}: key[{i}] must be UTC, got Kind={t.Kind}, t={t:O}.");
				}

			xs.Sort (( a, b ) => keyUtc (a).CompareTo (keyUtc (b)));

			EnsureStrictlyAscendingUtc (xs, keyUtc, seriesName);
			}
		}
	}
