using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Causal.ML.Daily;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Leakage.Daily
	{
	/// <summary>
	/// Интеграционный тест "поведение должно ломаться при разрушении train-сигнала":
	/// 1) shuffle train-лейблов → качество на OOS должно заметно упасть;
	/// 2) randomize train-фичей → качество на OOS должно стать близким к случайному.
	///
	/// Тест специально самодостаточный: не зависит от внешнего кэша свечей/старых бутстрапов,
	/// чтобы не маскировать ошибки инфраструктуры отсутствием данных.
	/// </summary>
	public sealed class DailyPipelineLeakageTests
		{
		private sealed class DailyRunResult
			{
			public required DateTime TrainUntilUtc { get; init; }
			public required List<(DateTime DateUtc, int TrueLabel, int PredLabel)> OosPreds { get; init; }
			public required double BaselineOosAccuracy { get; init; }
			}

		[Fact]
		public async Task DailyModel_OosQualityDrops_WhenTrainLabelsAreShuffled ()
			{
			var allRows = BuildSyntheticRows (
				startUtc: new DateTime (2022, 01, 01, 0, 0, 0, DateTimeKind.Utc),
				days: 700,
				seed: 123);

			var baseline = await RunDailyPipelineAsync (
				allRows,
				mutateTrain: null);

			Assert.False (double.IsNaN (baseline.BaselineOosAccuracy), "Baseline OOS accuracy is NaN.");
			Assert.True (baseline.BaselineOosAccuracy > 0.45, $"Baseline OOS accuracy too low: {baseline.BaselineOosAccuracy:0.000}");

			var shuffled = await RunDailyPipelineAsync (
				allRows,
				mutateTrain: ( rows, trainUntil ) => ShuffleTrainLabels (rows, trainUntil, seed: 777));

			var accShuffled = ComputeAccuracy (shuffled.OosPreds);

			Assert.True (
				accShuffled < baseline.BaselineOosAccuracy - 0.15,
				$"OOS accuracy with shuffled labels did not drop enough. baseline={baseline.BaselineOosAccuracy:0.000}, shuffled={accShuffled:0.000}");
			}

		[Fact]
		public async Task DailyModel_OosQualityDrops_WhenTrainFeaturesAreRandomized ()
			{
			var allRows = BuildSyntheticRows (
				startUtc: new DateTime (2022, 01, 01, 0, 0, 0, DateTimeKind.Utc),
				days: 700,
				seed: 42);

			var baseline = await RunDailyPipelineAsync (
				allRows,
				mutateTrain: null);

			Assert.False (double.IsNaN (baseline.BaselineOosAccuracy), "Baseline OOS accuracy is NaN.");
			Assert.True (baseline.BaselineOosAccuracy > 0.45, $"Baseline OOS accuracy too low: {baseline.BaselineOosAccuracy:0.000}");

			var randomized = await RunDailyPipelineAsync (
				allRows,
				mutateTrain: ( rows, trainUntil ) => RandomizeTrainFeatures (rows, trainUntil, seed: 999));

			var accRandom = ComputeAccuracy (randomized.OosPreds);

			Assert.True (
				accRandom < baseline.BaselineOosAccuracy - 0.15 && accRandom < 0.50,
				$"OOS accuracy with randomized features is suspiciously high. baseline={baseline.BaselineOosAccuracy:0.000}, randomized={accRandom:0.000}");
			}

		private static async Task<DailyRunResult> RunDailyPipelineAsync (
			List<LabeledCausalRow> allRows,
			Action<List<BacktestRecord>, DateTime>? mutateTrain )
			{
			if (allRows == null) throw new ArgumentNullException (nameof (allRows));
			if (allRows.Count == 0) throw new InvalidOperationException ("RunDailyPipelineAsync: пустой allRows.");

			// Стабильный порядок по времени.
			var ordered = allRows.OrderBy (r => r.ToCausalDateUtc()).ToList ();

			var maxDate = ordered[^1].Date;

			const int HoldoutDays = 120;
			var trainUntil = maxDate.AddDays (-HoldoutDays);

			var trainRows = TakeTrainRows (ordered, trainUntil);

			if (trainRows.Count < 100)
				{
				trainRows = ordered;
				trainUntil = ordered[^1].Date;
				}

			// Даём тесту возможность испортить train-сигнал ДО обучения.
			mutateTrain?.Invoke (allRows, trainUntil);

			// После мутации восстанавливаем консистентность:
			// - заново сортируем,
			// - заново берём trainRows,
			// - приводим Features к каузальному вектору (иначе тренировка/инференс могут разъехаться).
			ordered = allRows.OrderBy (r => r.ToCausalDateUtc()).ToList ();
			trainRows = TakeTrainRows (ordered, trainUntil);
			SyncFeaturesWithCausalVector (trainRows);

			var trainer = new ModelTrainer
				{
				DisableMoveModel = false,
				DisableDirNormalModel = false,
				DisableDirDownModel = true,
				DisableMicroFlatModel = false
				};

			var bundle = trainer.TrainAll (trainRows);

			var engine = new PredictionEngine (bundle);

			// OOS предсказания.
			var oos = new List<(DateTime, int, int)> ();

			foreach (var r in ordered)
				{
				if (r.ToCausalDateUtc() <= trainUntil)
					continue;

				var p = engine.PredictCausal (r.ToCausal ());
				oos.Add ((r.ToCausalDateUtc(), r.Forward.TrueLabel, p.Class));
				}

			var acc = ComputeAccuracy (oos);

			return await Task.FromResult (new DailyRunResult
				{
				TrainUntilUtc = trainUntil,
				OosPreds = oos,
				BaselineOosAccuracy = acc
				});
			}

		private static void SyncFeaturesWithCausalVector ( List<BacktestRecord> rows )
			{
			foreach (var r in rows)
				{
				// Инвариант: фичи, на которых учимся, должны совпадать с тем,
				// что реально подаётся в PredictCausal (через CausalDataRow.FeaturesVector).
				var v = r.ToCausal ().FeaturesVector;
				r.Causal.Features = v.ToArray ();
				}
			}

		private static List<BacktestRecord> TakeTrainRows ( List<BacktestRecord> orderedByDate, DateTime trainUntilUtc )
			{
			var train = new List<BacktestRecord> (orderedByDate.Count);
			foreach (var r in orderedByDate)
				{
				if (r.ToCausalDateUtc() <= trainUntilUtc)
					train.Add (r);
				else
					break;
				}
			return train;
			}

		private static double ComputeAccuracy ( List<(DateTime DateUtc, int TrueLabel, int PredLabel)> preds )
			{
			int total = preds.Count;
			if (total == 0) return double.NaN;

			int ok = 0;
			foreach (var x in preds)
				{
				if (x.PredLabel == x.TrueLabel) ok++;
				}
			return ok / (double) total;
			}

		private static void ShuffleTrainLabels ( List<BacktestRecord> rows, DateTime trainUntilUtc, int seed )
			{
			var train = new List<BacktestRecord> (rows.Count);
			foreach (var r in rows)
				{
				if (r.ToCausalDateUtc() <= trainUntilUtc)
					train.Add (r);
				}

			if (train.Count == 0)
				throw new InvalidOperationException ("[shuffle-labels] train-set is empty.");

			var labels = train.Select (r => r.Forward.TrueLabel).ToArray ();

			var rng = new Random (seed);
			for (int i = labels.Length - 1; i > 0; i--)
				{
				int j = rng.Next (i + 1);
				(labels[i], labels[j]) = (labels[j], labels[i]);
				}

			for (int i = 0; i < train.Count; i++)
				train[i].Label = labels[i];
			}

		private static void RandomizeTrainFeatures ( List<BacktestRecord> rows, DateTime trainUntilUtc, int seed )
			{
			var rng = new Random (seed);

			foreach (var r in rows)
				{
				if (r.ToCausalDateUtc() > trainUntilUtc)
					continue;

				var feats = r.Causal.Features;
				if (feats == null || feats.Length == 0)
					throw new InvalidOperationException ("[randomize-features] Features must be non-empty for train rows.");

				for (int i = 0; i < feats.Length; i++)
					feats[i] = rng.NextDouble () * 2.0 - 1.0; // [-1;1]
				}
			}

		private static List<BacktestRecord> BuildSyntheticRows ( DateTime startUtc, int days, int seed )
			{
			if (startUtc.Kind != DateTimeKind.Utc)
				throw new InvalidOperationException ("startUtc must be UTC.");

			var rng = new Random (seed);
			var rows = new List<BacktestRecord> (days);

			for (int i = 0; i < days; i++)
				{
				var date = startUtc.AddDays (i);

				// Синтетический сигнал: solRet1 определяет класс.
				// Это нужно, чтобы baseline-качество было выше случайного и тест имел смысл.
				double solRet1 = (rng.NextDouble () - 0.5) * 0.10; // [-5%; +5%]
				int label =
					solRet1 > 0.01 ? 2 :
					solRet1 < -0.01 ? 0 :
					1;

				var r = new BacktestRecord
					{
					Date = date,
					Label = label,

					RegimeDown = label == 0,
					IsMorning = true,

					SolRet30 = (rng.NextDouble () - 0.5) * 0.20,
					BtcRet30 = (rng.NextDouble () - 0.5) * 0.15,
					SolRet1 = solRet1,
					SolRet3 = solRet1 * 0.7,
					BtcRet1 = solRet1 * 0.3,
					BtcRet3 = solRet1 * 0.2,

					Fng = (rng.NextDouble () - 0.5) * 2.0,
					DxyChg30 = (rng.NextDouble () - 0.5) * 0.06,
					GoldChg30 = (rng.NextDouble () - 0.5) * 0.06,
					BtcVs200 = (rng.NextDouble () - 0.5) * 0.10,
					SolRsiCentered = (rng.NextDouble () - 0.5) * 40.0,
					RsiSlope3 = (rng.NextDouble () - 0.5) * 10.0,

					AtrPct = 0.02 + rng.NextDouble () * 0.02,
					DynVol = 0.5 + rng.NextDouble () * 1.0,
					MinMove = 0.01 + rng.NextDouble () * 0.02,

					TrendRet24h = (rng.NextDouble () - 0.5) * 0.08,
					TrendVol7d = 0.5 + rng.NextDouble () * 1.0,
					VolShiftRatio = 0.5 + rng.NextDouble () * 1.0,
					TrendAbs30 = rng.NextDouble () * 0.2,

					HardRegime = rng.NextDouble () < 0.2 ? 2 : 1,

					SolEma50 = 100 + rng.NextDouble () * 10,
					SolEma200 = 100 + rng.NextDouble () * 10,
					BtcEma50 = 100 + rng.NextDouble () * 10,
					BtcEma200 = 100 + rng.NextDouble () * 10,
					SolEma50vs200 = (rng.NextDouble () - 0.5) * 0.05,
					BtcEma50vs200 = (rng.NextDouble () - 0.5) * 0.05,
					};

				// Важный шаг: делаем Features строго эквивалентным тому,
				// что пойдёт в PredictCausal через CausalDataRow.
				var v = r.ToCausal ().FeaturesVector;
				r.Causal.Features = v.ToArray ();

				rows.Add (r);
				}

			return rows;
			}
		}
	}
