using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Infra;

namespace SolSignalModel1D_Backtest.Core.ML.SL
	{
	/// <summary>
	/// Контейнер датасета для SL-модели:
	/// - MorningRows — исходные дневные строки для утренних точек;
	/// - Samples — SL-сэмплы (long/short) для обучения.
	/// </summary>
	public sealed class SlDataset
		{
		/// <summary>
		/// Граница train-окна для SL-модели.
		/// Все сэмплы в этом датасете имеют baseline-выход <= TrainUntilUtc.
		/// </summary>
		public DateTime TrainUntilUtc { get; init; }

		/// <summary>
		/// Утренние дневные строки, по которым строились SL-сэмплы.
		/// Один день может давать несколько сэмплов (long/short),
		/// но сам DataRow здесь уникален по Date.
		/// </summary>
		public List<DataRow> MorningRows { get; init; } = new List<DataRow> ();

		/// <summary>
		/// Сырые SL-сэмплы (long/short), которыми фактически тренируется модель.
		/// </summary>
		public List<SlHitSample> Samples { get; init; } = new List<SlHitSample> ();
		}

	/// <summary>
	/// Builder SL-датасета поверх низкоуровневого SlOfflineBuilder.
	/// Задача:
	/// - использовать только дни с Date <= trainUntil;
	/// - выкинуть любые сэмплы, у которых baseline-выход залезает за trainUntil.
	///
	/// Таким образом, train-часть SL-модели строго future-blind
	/// и не использует path-based информацию из OOS-участка.
	/// </summary>
	public static class SlDatasetBuilder
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		/// <summary>
		/// Строит SlDataset для обучения SL-модели.
		/// </summary>
		/// <param name="rows">
		/// Все дневные строки RowBuilder'а (включая IsMorning/MinMove/RegimeDown).
		/// </param>
		/// <param name="sol1h">Вся 1h-история SOL (для фичей). Может быть null.</param>
		/// <param name="sol1m">Вся 1m-история SOL (для path-based факта).</param>
		/// <param name="sol6hDict">Словарь 6h-свечей SOL по Date (NY-утро).</param>
		/// <param name="trainUntil">
		/// Граница train-окна. Дни, у которых baseline-выход &gt; trainUntil,
		/// не попадают в датасет.
		/// </param>
		/// <param name="tpPct">TP (в долях, 0.03 = 3%).</param>
		/// <param name="slPct">SL (в долях, 0.05 = 5%).</param>
		/// <param name="strongSelector">
		/// Кастомная логика strong/weak для SL-фич; если null — все дни сильные.
		/// </param>
		public static SlDataset Build (
			List<DataRow> rows,
			IReadOnlyList<Candle1h>? sol1h,
			IReadOnlyList<Candle1m>? sol1m,
			Dictionary<DateTime, Candle6h> sol6hDict,
			DateTime trainUntil,
			double tpPct,
			double slPct,
			Func<DataRow, bool>? strongSelector )
			{
			if (rows == null) throw new ArgumentNullException (nameof (rows));
			if (sol6hDict == null) throw new ArgumentNullException (nameof (sol6hDict));
			if (sol1m == null) throw new ArgumentNullException (nameof (sol1m));

			// 1. Берём только дни с Date <= trainUntil.
			// Это гарантирует, что EntryUtc сэмпла не позже trainUntil.
			var rowsTrain = rows
				.Where (r => r.Date <= trainUntil)
				.OrderBy (r => r.Date)
				.ToList ();

			if (rowsTrain.Count == 0)
				{
				return new SlDataset
					{
					TrainUntilUtc = trainUntil,
					MorningRows = new List<DataRow> (),
					Samples = new List<SlHitSample> ()
					};
				}

			// 2. Низкоуровневая генерация SL-сэмплов (long/short) поверх rowsTrain.
			// Внутри SlOfflineBuilder.Build:
			// - используется только rowsTrain (IsMorning/MinMove);
			// - 1h/1m/6h берутся "как есть" по всей истории.
			var allSamples = SlOfflineBuilder.Build (
				rows: rowsTrain,
				sol1h: sol1h,
				sol1m: sol1m,
				sol6hDict: sol6hDict,
				tpPct: tpPct,
				slPct: slPct,
				strongSelector: strongSelector
			);

			if (allSamples.Count == 0)
				{
				return new SlDataset
					{
					TrainUntilUtc = trainUntil,
					MorningRows = new List<DataRow> (),
					Samples = new List<SlHitSample> ()
					};
				}

			// 3. Фильтруем сэмплы: baseline-выход не должен залезать за trainUntil.
			// path-based логика внутри SlOfflineBuilder использует окна до
			// ComputeBaselineExitUtc(entry), здесь мы просто режем по этой границе.
			var filteredSamples = new List<SlHitSample> (allSamples.Count);

			foreach (var s in allSamples)
				{
				var exit = Windowing.ComputeBaselineExitUtc (s.EntryUtc, NyTz);

				if (exit <= trainUntil)
					{
					filteredSamples.Add (s);
					}
				}

			// 4. Собираем утренние строки, по которым остались сэмплы.
			var morningByDate = rowsTrain
				.Where (r => r.IsMorning)
				.GroupBy (r => r.Date)
				.ToDictionary (g => g.Key, g => g.First ());

			var morningRows = new List<DataRow> ();

			foreach (var s in filteredSamples)
				{
				if (morningByDate.TryGetValue (s.EntryUtc, out var row))
					{
					morningRows.Add (row);
					}
				}

			// Делаем уникальными по Date (иначе long/short дадут дубликаты одного дня).
			var distinctMorning = morningRows
				.OrderBy (r => r.Date)
				.GroupBy (r => r.Date)
				.Select (g => g.First ())
				.ToList ();

			return new SlDataset
				{
				TrainUntilUtc = trainUntil,
				MorningRows = distinctMorning,
				Samples = filteredSamples
				};
			}
		}
	}
