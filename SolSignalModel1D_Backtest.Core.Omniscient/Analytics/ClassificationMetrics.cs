using SolSignalModel1D_Backtest.Core.Omniscient.Omniscient.Data;

namespace SolSignalModel1D_Backtest.Core.Omniscient.Analytics
	{
	// один класс на весь отчёт
	public sealed class ClassificationReport
		{
		public double Accuracy { get; set; }
		public double MacroF1 { get; set; }
		public double MicroF1 { get; set; }
		public List<ClassReport> PerClass { get; set; } = new ();
		}

	// по каждому классу
	public sealed class ClassReport
		{
		public int Label { get; set; }
		public int Support { get; set; }
		public double Precision { get; set; }
		public double Recall { get; set; }
		public double F1 { get; set; }
		}

	// для "мягкой" точности (рост == боковикРост и т.п.)
	public sealed class LenientReport
		{
		public int Correct { get; set; }
		public int Total { get; set; }
		public double Accuracy { get; set; }
		}

	public static class ClassificationMetrics
		{
		/// <summary>
		/// Основная метрика: accuracy, macro/micro F1, per-class.
		/// useMicro=true означает: если модель сказала "боковик + микроВверх",
		/// то мы это считаем как "рост" при подсчёте.
		/// </summary>
		public static ClassificationReport Compute ( List<BacktestRecord> records, bool useMicro )
			{
			// превращаем предсказания в "финальный" класс
			var preds = records.Select (r => (Truth: r.TrueLabel, Pred: GetPredictedLabel (r, useMicro))).ToList ();

			int total = preds.Count;
			int correct = preds.Count (p => p.Truth == p.Pred);

			// классы 0,1,2
			var labels = new[] { 0, 1, 2 };
			var perClass = new List<ClassReport> ();
			double sumF1 = 0.0;

			// для micro-F1 нужно посчитать глобальные TP/FP/FN
			int globalTp = 0, globalFp = 0, globalFn = 0;

			foreach (var label in labels)
				{
				int tp = preds.Count (p => p.Truth == label && p.Pred == label);
				int fp = preds.Count (p => p.Truth != label && p.Pred == label);
				int fn = preds.Count (p => p.Truth == label && p.Pred != label);
				int support = preds.Count (p => p.Truth == label);

				double prec = tp + fp == 0 ? 0.0 : (double) tp / (tp + fp);
				double rec = support == 0 ? 0.0 : (double) tp / support;
				double f1 = (prec + rec) == 0 ? 0.0 : 2.0 * prec * rec / (prec + rec);

				perClass.Add (new ClassReport
					{
					Label = label,
					Support = support,
					Precision = prec,
					Recall = rec,
					F1 = f1
					});

				sumF1 += f1;

				globalTp += tp;
				globalFp += fp;
				globalFn += fn;
				}

			double macroF1 = sumF1 / labels.Length;
			double microPrec = (globalTp + globalFp) == 0 ? 0.0 : (double) globalTp / (globalTp + globalFp);
			double microRec = (globalTp + globalFn) == 0 ? 0.0 : (double) globalTp / (globalTp + globalFn);
			double microF1 = (microPrec + microRec) == 0 ? 0.0 : 2.0 * microPrec * microRec / (microPrec + microRec);

			return new ClassificationReport
				{
				Accuracy = total == 0 ? 0.0 : (double) correct / total,
				MacroF1 = macroF1,
				MicroF1 = microF1,
				PerClass = perClass
				};
			}

		/// <summary>
		/// Мягкая точность: рост считается ок и для "боковикРост", и наоборот.
		/// Мы не пересчитываем F1 — только долю таких “норм” попаданий.
		/// </summary>
		public static LenientReport ComputeLenient ( List<BacktestRecord> records )
			{
			int correct = 0;
			int total = 0;

			foreach (var r in records)
				{
				total++;

				bool ok = false;

				// точное попадание
				if (r.TrueLabel == r.PredLabel)
					{
					ok = true;
					}
				else
					{
					// мягкие правила
					// если модель сказала рост (2), а по факту боковик с микро вверх
					if (r.PredLabel == 2 && r.TrueLabel == 1 && r.FactMicroUp)
						ok = true;

					// если модель сказала обвал (0), а по факту боковик с микро вниз
					if (r.PredLabel == 0 && r.TrueLabel == 1 && r.FactMicroDown)
						ok = true;

					// если модель сказала боковик+микроUp, а по факту рост
					if (r.PredLabel == 1 && r.PredMicroUp && r.TrueLabel == 2)
						ok = true;

					// если модель сказала боковик+микроDown, а по факту обвал
					if (r.PredLabel == 1 && r.PredMicroDown && r.TrueLabel == 0)
						ok = true;
					}

				if (ok) correct++;
				}

			return new LenientReport
				{
				Correct = correct,
				Total = total,
				Accuracy = total == 0 ? 0.0 : (double) correct / total
				};
			}

		/// <summary>
		/// Преобразуем предсказание в “микро-осознанный” класс, если надо.
		/// </summary>
		private static int GetPredictedLabel ( BacktestRecord r, bool useMicro )
			{
			if (!useMicro)
				return r.PredLabel;

			// если модель сказала "боковик", но ещё и направление — считаем как направление
			if (r.PredLabel == 1)
				{
				if (r.PredMicroUp) return 2;
				if (r.PredMicroDown) return 0;
				}

			return r.PredLabel;
			}
		}
	}
