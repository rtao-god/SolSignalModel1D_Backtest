using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Trading.Evaluator;
using SolSignalModel1D_Backtest.SanityChecks.SanityChecks;

namespace SolSignalModel1D_Backtest.SanityChecks.SanityChecks.Leakage.SL
	{
	/// <summary>
	/// Sanity-проверки для SL-модели:
	/// - проверка диапазона SlProb;
	/// - оценка TPR/FPR по path-based исходу сделки (через HourlyTradeEvaluator);
	/// - сравнение train vs OOS.
	/// </summary>
	public static class SlLeakageChecks
		{
		/// <summary>
		/// Основная проверка SL-слоя по текущему контексту.
		/// </summary>
		public static SelfCheckResult CheckSlLayer ( SelfCheckContext ctx )
			{
			if (ctx == null) throw new ArgumentNullException (nameof (ctx));

			var records = ctx.Records ?? Array.Empty<PredictionRecord> ();
			var candles1h = ctx.SolAll1h ?? Array.Empty<Candle1h> ();

			if (records.Count == 0 || candles1h.Count == 0)
				{
				return SelfCheckResult.Ok ("[sl] нет данных для SL-слоя (records или 1h-профиль пустой).");
				}

			var samples = new List<SlSample> ();

			foreach (var rec in records)
				{
				// SL-модель применяется только если есть торговый сигнал.
				bool goLong = rec.PredLabel == 2 || (rec.PredLabel == 1 && rec.PredMicroUp);
				bool goShort = rec.PredLabel == 0 || (rec.PredLabel == 1 && rec.PredMicroDown);

				if (!goLong && !goShort)
					continue;

				// SlProb/SlHighDecision должны быть в адекватном диапазоне.
				double slProb = rec.SlProb;
				bool slHigh = rec.SlHighDecision;

				if (slProb < 0.0 || slProb > 1.0)
					{
					var badRange = new SelfCheckResult
						{
						Success = false,
						Summary = $"[sl] обнаружена SlProb вне диапазона [0,1]: {slProb:0.000} на дате {rec.DateUtc:O}."
						};
					badRange.Errors.Add ("[sl] SlProb должен лежать в [0,1].");
					return badRange;
					}

				double dayMinMove = rec.MinMove > 0 ? rec.MinMove : 0.02;
				bool strongSignal = rec.PredLabel == 0 || rec.PredLabel == 2;

				// Path-based исход сделки по текущей торговой схеме.
				var outcome = HourlyTradeEvaluator.EvaluateOne (
					candles1h,
					entryUtc: rec.DateUtc,
					goLong: goLong,
					goShort: goShort,
					entryPrice: rec.Entry,
					dayMinMove: dayMinMove,
					strongSignal: strongSignal,
					nyTz: ctx.NyTz);

				if (outcome.Result != HourlyTradeResult.SlFirst &&
					outcome.Result != HourlyTradeResult.TpFirst)
					{
					// Неоднозначный или пустой исход — для оценки SL не используем.
					continue;
					}

				bool trueHighRisk = outcome.Result == HourlyTradeResult.SlFirst;

				samples.Add (new SlSample
					{
					DateUtc = rec.DateUtc,
					SlProb = slProb,
					SlHighDecision = slHigh,
					TrueHighRisk = trueHighRisk
					});
				}

			if (samples.Count == 0)
				{
				return SelfCheckResult.Ok ("[sl] нет сделок с однозначным исходом TP/SL для оценки SL-слоя.");
				}

			// Если все SlProb≈0 и ни одного SlHighDecision, скорее всего SL-модель не запускалась.
			bool allDefault = samples.All (s => s.SlProb == 0.0 && !s.SlHighDecision);
			if (allDefault)
				{
				return SelfCheckResult.Ok (
					"[sl] все SlProb≈0 и SlHighDecision=false — похоже, SL-модель не применялась, sanity-проверка пропущена.");
				}

			if (samples.Count < 50)
				{
				return SelfCheckResult.Ok (
					$"[sl] недостаточно сделок с однозначным исходом для оценки SL ({samples.Count}), sanity-проверка пропущена.");
				}

			var train = samples.Where (p => p.DateUtc <= ctx.TrainUntilUtc).ToList ();
			var oos = samples.Where (p => p.DateUtc > ctx.TrainUntilUtc).ToList ();

			var warnings = new List<string> ();
			var errors = new List<string> ();

			if (train.Count < 50)
				{
				warnings.Add ($"[sl] train-выборка для SL мала ({train.Count}), статистика шумная.");
				}

			if (oos.Count == 0)
				{
				warnings.Add ("[sl] OOS-часть для SL пуста (нет сделок после _trainUntilUtc).");
				}

			var trainMetrics = ComputeMetrics (train);
			var oosMetrics = ComputeMetrics (oos);
			var allMetrics = ComputeMetrics (samples);

			// Подозрительно хороший OOS — возможная утечка.
			if (oos.Count >= 100 && oosMetrics.Tpr > 0.90 && oosMetrics.Fpr < 0.10)
				{
				errors.Add (
					$"[sl] OOS TPR={oosMetrics.Tpr:P1}, FPR={oosMetrics.Fpr:P1} при {oos.Count} сделок — подозрение на утечку в SL-слое.");
				}

			// SL вообще не информативна: FPR ~ TPR.
			if (allMetrics.Samples >= 50 && Math.Abs (allMetrics.Tpr - allMetrics.Fpr) < 0.05)
				{
				warnings.Add (
					$"[sl] SL-модель почти не отличает high-risk от low-risk: TPR={allMetrics.Tpr:P1}, FPR={allMetrics.Fpr:P1}.");
				}

			// Количество high-decisions.
			int totalPredHigh = samples.Count (p => p.SlHighDecision);
			if (totalPredHigh == 0)
				{
				warnings.Add ("[sl] SlHighDecision никогда не срабатывает — порог риска может быть слишком жёстким.");
				}

			string summary =
				$"[sl] samples={samples.Count}, train={train.Count}, oos={oos.Count}, " +
				$"TPR_all={allMetrics.Tpr:P1}, FPR_all={allMetrics.Fpr:P1}, " +
				$"TPR_oos={oosMetrics.Tpr:P1}, FPR_oos={oosMetrics.Fpr:P1}";

			var res = new SelfCheckResult
				{
				Success = errors.Count == 0,
				Summary = summary
				};
			res.Errors.AddRange (errors);
			res.Warnings.AddRange (warnings);
			return res;
			}

		/// <summary>Внутренний сэмпл для SL-проверок.</summary>
		private sealed class SlSample
			{
			public DateTime DateUtc { get; set; }
			public double SlProb { get; set; }
			public bool SlHighDecision { get; set; }
			public bool TrueHighRisk { get; set; }
			}

		private readonly struct SlMetrics
			{
			public SlMetrics ( int samples, int pos, int neg, int tp, int fp )
				{
				Samples = samples;
				Pos = pos;
				Neg = neg;
				Tp = tp;
				Fp = fp;

				Tpr = pos > 0 ? (double) Tp / pos : 0.0;
				Fpr = neg > 0 ? (double) Fp / neg : 0.0;
				}

			public int Samples { get; }
			public int Pos { get; }
			public int Neg { get; }
			public int Tp { get; }
			public int Fp { get; }
			public double Tpr { get; }
			public double Fpr { get; }
			}

		private static SlMetrics ComputeMetrics ( IReadOnlyList<SlSample> samples )
			{
			if (samples == null || samples.Count == 0)
				return new SlMetrics (0, 0, 0, 0, 0);

			int pos = 0;
			int neg = 0;
			int tp = 0;
			int fp = 0;

			for (int i = 0; i < samples.Count; i++)
				{
				var s = samples[i];
				if (s.TrueHighRisk)
					{
					pos++;
					if (s.SlHighDecision)
						tp++;
					}
				else
					{
					neg++;
					if (s.SlHighDecision)
						fp++;
					}
				}

			return new SlMetrics (samples.Count, pos, neg, tp, fp);
			}
		}
	}
