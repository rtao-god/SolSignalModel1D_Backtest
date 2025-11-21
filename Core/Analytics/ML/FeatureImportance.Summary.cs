namespace SolSignalModel1D_Backtest.Core.Analytics.ML
	{
	/// <summary>
	/// Глобальная сводка по всем зарегистрированным PFI-снапшотам:
	/// - краткий обзор моделей (AUC, размер, рейтинг);
	/// - топ-фичи по каждой модели;
	/// - глобальный список ключевых фич по всем моделям.
	/// Вся математика PFI уже посчитана в FeatureImportanceCore / FeatureImportanceAnalyzer,
	/// здесь только агрегация и форматированный вывод.
	/// </summary>
	internal static class FeatureImportanceSummary
		{
		/// <summary>
		/// Внутренний агрегат по одной фиче через все модели.
		/// </summary>
		private sealed class GlobalFeatureRow
			{
			public string Name { get; set; } = string.Empty;
			public int Models { get; set; }
			public double ImpSum { get; set; }
			public double ImpMax { get; set; }
			public double CorrScoreSum { get; set; }

			public double ImpAvg { get; set; }
			public double CorrScoreAvg { get; set; }
			}

		/// <summary>
		/// Точка входа: печатает сводку по всем снапшотам.
		/// </summary>
		public static void PrintGlobalSummary (
			int topPerModel = 5,
			int topGlobalFeatures = 15,
			double importanceThreshold = 0.003 )
			{
			var snapshots = FeatureImportanceSnapshots.GetSnapshots ();
			if (snapshots == null || snapshots.Count == 0)
				{
				Console.WriteLine ("[pfi:summary] no models analyzed (no snapshots registered).");
				return;
				}

			Console.WriteLine ();
			Console.WriteLine ($"[pfi:summary] models analyzed = {snapshots.Count}");
			Console.WriteLine ();

			PrintModelsOverview (snapshots);
			Console.WriteLine ();

			PrintTopFeaturesPerModel (snapshots, topPerModel, importanceThreshold);
			Console.WriteLine ();

			PrintGlobalTopFeatures (snapshots, topGlobalFeatures, importanceThreshold);
			Console.WriteLine ();
			}

		// ==================== MODELS OVERVIEW ====================

		private static void PrintModelsOverview ( IReadOnlyList<FeatureImportanceSnapshot> snapshots )
			{
			Console.WriteLine ("-- Models overview --");
			Console.WriteLine (" model-tag                 AUC    rating   rows   pos   neg");

			foreach (var snap in snapshots)
				{
				var stats = snap.Stats;
				int pos = 0, neg = 0, rows = 0;

				if (stats != null && stats.Count > 0)
					{
					// CountPos / CountNeg одинаковые для всех фич — берём из первой.
					pos = stats[0].CountPos;
					neg = stats[0].CountNeg;
					rows = pos + neg;
					}

				double auc = snap.BaselineAuc;
				double aucPct = auc * 100.0;

				string rating = GetModelRating (auc);
				var oldColor = Console.ForegroundColor;
				Console.ForegroundColor = GetModelColor (rating);

				Console.WriteLine (
					"{0,-24} {1,6:F1}% {2,8} {3,6} {4,5} {5,5}",
					FeatureImportanceCore.TruncateName (snap.Tag, 24),
					aucPct,
					rating,
					rows,
					pos,
					neg);

				Console.ForegroundColor = oldColor;
				}
			}

		/// <summary>
		/// Грубый вердикт по AUC модели.
		/// </summary>
		private static string GetModelRating ( double auc )
			{
			// Можно подстроить границы под себя.
			if (auc >= 0.80) return "OK";     // хорошо
			if (auc >= 0.70) return "MID";    // терпимо
			return "WEAK";                    // плохо / шум
			}

		private static ConsoleColor GetModelColor ( string rating ) =>
			rating switch
				{
					"OK" => ConsoleColor.Green,
					"MID" => ConsoleColor.Yellow,
					"WEAK" => ConsoleColor.Red,
					_ => ConsoleColor.Gray
					};

		// ==================== TOP FEATURES PER MODEL ====================

		private static void PrintTopFeaturesPerModel (
			IReadOnlyList<FeatureImportanceSnapshot> snapshots,
			int topPerModel,
			double importanceThreshold )
			{
			Console.WriteLine ("-- Top features per model --");
			Console.WriteLine (" model-tag                 feature                imp(pp)  dMean(%)  corr_s(%)");

			foreach (var snap in snapshots)
				{
				if (snap.Stats == null || snap.Stats.Count == 0)
					continue;

				var stats = snap.Stats;
				int printed = 0;
				bool firstRow = true;

				foreach (var s in stats)
					{
					if (s.ImportanceAuc < importanceThreshold)
						continue;

					double impPct = s.ImportanceAuc * 100.0;     // важность в п.п. AUC
					double dMeanPct = s.DeltaMean * 100.0;       // разница средних в %
					double corrScorePct = s.CorrScore * 100.0;   // корреляция в %

					var oldColor = Console.ForegroundColor;
					Console.ForegroundColor = GetFeatureColor (impPct, dMeanPct);

					string modelCol = firstRow
						? FeatureImportanceCore.TruncateName (snap.Tag, 24)
						: new string (' ', 24);
					firstRow = false;

					Console.WriteLine (
						"{0} {1,-22} {2,8:F2}% {3,9:F2}% {4,9:F1}%",
						modelCol,
						FeatureImportanceCore.TruncateName (s.Name, 22),
						impPct,
						dMeanPct,
						corrScorePct);

					Console.ForegroundColor = oldColor;

					printed++;
					if (printed >= topPerModel)
						break;
					}
				}
			}

		/// <summary>
		/// Цвет фичи в топе модели:
		/// - зелёный / красный для очень важных (imp ≥ 5 п.п.) с разным знаком dMean;
		/// - циан для средне-важных;
		/// - серый для слабых.
		/// </summary>
		private static ConsoleColor GetFeatureColor ( double impPct, double dMeanPct )
			{
			if (impPct >= 5.0)
				{
				// Очень сильное влияние — подсвечиваем по знаку.
				return dMeanPct >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
				}

			if (impPct >= 2.0)
				{
				// Средняя важность.
				return ConsoleColor.Cyan;
				}

			return ConsoleColor.Gray;
			}

		// ==================== GLOBAL TOP FEATURES ====================

		private static void PrintGlobalTopFeatures (
			IReadOnlyList<FeatureImportanceSnapshot> snapshots,
			int topGlobalFeatures,
			double importanceThreshold )
			{
			var dict = new Dictionary<string, GlobalFeatureRow> (StringComparer.Ordinal);

			// Агрегируем по имени фичи: сколько моделей, средняя важность и средняя corr_s.
			foreach (var snap in snapshots)
				{
				if (snap.Stats == null) continue;

				foreach (var s in snap.Stats)
					{
					if (s.ImportanceAuc < importanceThreshold)
						continue;

					if (!dict.TryGetValue (s.Name, out var row))
						{
						row = new GlobalFeatureRow { Name = s.Name };
						dict.Add (s.Name, row);
						}

					row.Models++;
					row.ImpSum += s.ImportanceAuc;
					if (s.ImportanceAuc > row.ImpMax)
						row.ImpMax = s.ImportanceAuc;
					row.CorrScoreSum += s.CorrScore;
					}
				}

			var list = new List<GlobalFeatureRow> (dict.Values);
			foreach (var r in list)
				{
				if (r.Models > 0)
					{
					r.ImpAvg = r.ImpSum / r.Models;
					r.CorrScoreAvg = r.CorrScoreSum / r.Models;
					}
				}

			// Сортируем по средней важности.
			list.Sort (( a, b ) => b.ImpAvg.CompareTo (a.ImpAvg));

			if (topGlobalFeatures > 0 && topGlobalFeatures < list.Count)
				list = list.GetRange (0, topGlobalFeatures);

			Console.WriteLine ("-- Global top features across models --");
			Console.WriteLine ("  #  feature               models  imp_avg  imp_max   sign  corr_s_avg  rating");

			int rank = 1;
			foreach (var r in list)
				{
				double impAvgPct = r.ImpAvg * 100.0;
				double impMaxPct = r.ImpMax * 100.0;
				double corrAvgPct = r.CorrScoreAvg * 100.0;

				string signBucket = GetSignBucket (r.CorrScoreAvg);
				string rating = GetGlobalFeatureRating (impAvgPct, r.Models);

				var oldColor = Console.ForegroundColor;
				Console.ForegroundColor = GetGlobalFeatureColor (rating);

				Console.WriteLine (
					"{0,3} {1,-20} {2,6} {3,8:F2}% {4,8:F2}% {5,6} {6,10:F1}% {7,8}",
					rank,
					FeatureImportanceCore.TruncateName (r.Name, 20),
					r.Models,
					impAvgPct,
					impMaxPct,
					signBucket,
					corrAvgPct,
					rating);

				Console.ForegroundColor = oldColor;
				rank++;
				}
			}

		/// <summary>
		/// Бакеты по знаку средней корреляции.
		/// </summary>
		private static string GetSignBucket ( double corr )
			{
			if (corr >= 0.3) return "++";
			if (corr >= 0.1) return "+";
			if (corr <= -0.3) return "--";
			if (corr <= -0.1) return "-";
			return "+/-";
			}

		/// <summary>
		/// Грубый вердикт по фиче:
		/// - CORE: высокая средняя важность и встречается минимум в 3 моделях;
		/// - MID: средняя важность и ≥2 модели;
		/// - LOCAL: локальная фича (важна, но мало где).
		/// </summary>
		private static string GetGlobalFeatureRating ( double impAvgPct, int models )
			{
			if (impAvgPct >= 5.0 && models >= 3) return "CORE";
			if (impAvgPct >= 2.0 && models >= 2) return "MID";
			return "LOCAL";
			}

		private static ConsoleColor GetGlobalFeatureColor ( string rating ) =>
			rating switch
				{
					"CORE" => ConsoleColor.Green,
					"MID" => ConsoleColor.Yellow,
					"LOCAL" => ConsoleColor.DarkGray,
					_ => ConsoleColor.Gray
					};
		}
	}
