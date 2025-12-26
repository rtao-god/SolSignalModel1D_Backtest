using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using SolSignalModel1D_Backtest.Core.ML.Shared;

namespace SolSignalModel1D_Backtest.Core.ML.SL
	{
	/// <summary>
	/// PFI + direction для SL-модели.
	/// Инварианты:
	/// - строго SlSchema.FeatureCount измерений;
	/// - ошибки данных/контракта не маскируются;
	/// - метрика важности: деградация AUC при пермутации одного признака.
	/// </summary>
	public static class SlPfiAnalyzer
		{
		private sealed class Row
			{
			public bool Label { get; set; }

			[VectorType (SlSchema.FeatureCount)]
			public float[] Features { get; set; } = new float[SlSchema.FeatureCount];
			}

		public static void LogBinaryPfiWithDirection (
			MLContext ml,
			ITransformer model,
			IEnumerable<SlHitSample> evalSamples,
			string tag = "sl-eval",
			int permutationCount = 3 )
			{
			if (ml == null) throw new ArgumentNullException (nameof (ml));
			if (model == null) throw new ArgumentNullException (nameof (model));
			if (evalSamples == null) throw new ArgumentNullException (nameof (evalSamples));
			if (permutationCount <= 0) throw new ArgumentOutOfRangeException (nameof (permutationCount));

			var list = evalSamples.ToList ();
			if (list.Count == 0)
				throw new InvalidOperationException ($"[pfi:{tag}] SL eval dataset is empty.");

			// Строго валидируем 11-мерность (никакого паддинга/обрезки).
			for (int i = 0; i < list.Count; i++)
				{
				var s = list[i];
				if (s == null)
					throw new InvalidOperationException ($"[pfi:{tag}] evalSamples contains null at idx={i}.");

				if (s.Features == null)
					throw new InvalidOperationException ($"[pfi:{tag}] SlHitSample.Features is null (idx={i}, entry={s.EntryUtc:O}).");

				if (s.Features.Length != SlSchema.FeatureCount)
					{
					throw new InvalidOperationException (
						$"[pfi:{tag}] SlHitSample.Features length mismatch at idx={i}, entry={s.EntryUtc:O}: " +
						$"got={s.Features.Length}, expected={SlSchema.FeatureCount}.");
					}
				}

			var rows = new List<Row> (list.Count);
			foreach (var s in list)
				{
				// Копируем, чтобы исключить внешние мутации массива фич во время расчёта метрик.
				var feats = new float[SlSchema.FeatureCount];
				Array.Copy (s.Features, feats, SlSchema.FeatureCount);

				rows.Add (new Row
					{
					Label = s.Label,
					Features = feats
					});
				}

			var (posCount, negCount) = CountClasses (rows);
			if (posCount == 0 || negCount == 0)
				{
				throw new InvalidOperationException (
					$"[pfi:{tag}] AUC is undefined because dataset has a single class: pos={posCount}, neg={negCount}.");
				}

			double baselineAuc = ComputeAuc (ml, model, rows);

			var means1 = new double[SlSchema.FeatureCount];
			var means0 = new double[SlSchema.FeatureCount];
			ComputeMeansByClass (rows, means1, means0);

			Console.WriteLine ();
			Console.WriteLine ($"===== PFI + direction (SL) [{tag}] =====");
			Console.WriteLine ($" baseline AUC = {baselineAuc:0.####}");
			Console.WriteLine (" idx  feature                imp(AUC)   dAUC    mean[1]    mean[0]   d(1-0)  corr_y   pos   neg");

			for (int featIdx = 0; featIdx < SlSchema.FeatureCount; featIdx++)
				{
				string name =
					(featIdx < SlFeatureSchema.Names.Length ? SlFeatureSchema.Names[featIdx] : $"f{featIdx}");

				// Важность = baselineAuc - meanAuc(после пермутаций).
				double meanPermAuc = ComputeMeanPermutedAuc (
					ml,
					model,
					rows,
					featIdx,
					permutationCount);

				double imp = baselineAuc - meanPermAuc;

				double m1 = means1[featIdx];
				double m0 = means0[featIdx];
				double d = m1 - m0;

				double corrY = PearsonCorrWithLabelOrNaN (rows, featIdx);

				Console.WriteLine (
					$"{featIdx,4}  {name,-20}  {imp,8:0.####}  {imp,6:0.####}  {m1,9:0.####}  {m0,9:0.####}  {d,7:0.####}  {corrY,6:0.###}  {posCount,4}  {negCount,4}");
				}
			}

		private static double ComputeAuc ( MLContext ml, ITransformer model, List<Row> rows )
			{
			var data = ml.Data.LoadFromEnumerable (rows);
			var scored = model.Transform (data);

			// Evaluate сам выбросит понятную ошибку, если в scored нет нужных колонок.
			var metrics = ml.BinaryClassification.Evaluate (
				scored,
				labelColumnName: nameof (Row.Label));

			if (double.IsNaN (metrics.AreaUnderRocCurve) || double.IsInfinity (metrics.AreaUnderRocCurve))
				throw new InvalidOperationException ($"[pfi] AUC is not a finite number: {metrics.AreaUnderRocCurve}.");

			return metrics.AreaUnderRocCurve;
			}

		private static double ComputeMeanPermutedAuc (
			MLContext ml,
			ITransformer model,
			List<Row> baseRows,
			int featIdx,
			int permutationCount )
			{
			// Пермутация делается только для одного столбца, остальные фичи неизменны.
			var col = new float[baseRows.Count];
			for (int r = 0; r < baseRows.Count; r++)
				col[r] = baseRows[r].Features[featIdx];

			double sum = 0.0;

			for (int p = 0; p < permutationCount; p++)
				{
				var rng = new Random (unchecked(123_456_789 + featIdx * 10_000 + p));

				// Fisher–Yates по индексу, чтобы не аллоцировать лишние массивы на каждый swap.
				var idx = Enumerable.Range (0, col.Length).ToArray ();
				for (int i = idx.Length - 1; i > 0; i--)
					{
					int j = rng.Next (i + 1);
					(idx[i], idx[j]) = (idx[j], idx[i]);
					}

				var permRows = new List<Row> (baseRows.Count);
				for (int r = 0; r < baseRows.Count; r++)
					{
					var src = baseRows[r];

					// Копия вектора нужна, чтобы пермутация не затронула другие прогонки/фичи.
					var feats = new float[SlSchema.FeatureCount];
					Array.Copy (src.Features, feats, SlSchema.FeatureCount);

					feats[featIdx] = col[idx[r]];

					permRows.Add (new Row
						{
						Label = src.Label,
						Features = feats
						});
					}

				sum += ComputeAuc (ml, model, permRows);
				}

			return sum / permutationCount;
			}

		private static (int pos, int neg) CountClasses ( List<Row> rows )
			{
			int pos = 0;
			for (int i = 0; i < rows.Count; i++)
				{
				if (rows[i].Label) pos++;
				}
			return (pos, rows.Count - pos);
			}

		private static void ComputeMeansByClass ( List<Row> rows, double[] mean1, double[] mean0 )
			{
			int c1 = 0;
			int c0 = 0;

			for (int r = 0; r < rows.Count; r++)
				{
				var row = rows[r];
				var f = row.Features;

				if (row.Label)
					{
					c1++;
					for (int i = 0; i < SlSchema.FeatureCount; i++)
						mean1[i] += f[i];
					}
				else
					{
					c0++;
					for (int i = 0; i < SlSchema.FeatureCount; i++)
						mean0[i] += f[i];
					}
				}

			if (c1 > 0)
				{
				for (int i = 0; i < SlSchema.FeatureCount; i++)
					mean1[i] /= c1;
				}

			if (c0 > 0)
				{
				for (int i = 0; i < SlSchema.FeatureCount; i++)
					mean0[i] /= c0;
				}
			}

		private static double PearsonCorrWithLabelOrNaN ( List<Row> rows, int featIdx )
			{
			// corr(feature, label), label ∈ {0,1}.
			// Если feature константна → корреляция не определена (NaN), чтобы не маскировать ситуацию.
			double sumX = 0, sumY = 0, sumXX = 0, sumYY = 0, sumXY = 0;
			int n = rows.Count;

			for (int i = 0; i < n; i++)
				{
				double x = rows[i].Features[featIdx];
				double y = rows[i].Label ? 1.0 : 0.0;

				sumX += x;
				sumY += y;
				sumXX += x * x;
				sumYY += y * y;
				sumXY += x * y;
				}

			double cov = (n * sumXY) - (sumX * sumY);
			double varX = (n * sumXX) - (sumX * sumX);
			double varY = (n * sumYY) - (sumY * sumY);

			if (varY <= 0)
				{
				// Случай single-class должен быть отфильтрован выше; здесь оставлено как защита контракта.
				return double.NaN;
				}

			if (varX <= 0)
				return double.NaN;

			return cov / Math.Sqrt (varX * varY);
			}
		}
	}
