namespace SolSignalModel1D_Backtest.Core.Analytics.ML
	{
	/// <summary>
	/// Глобальная сводка по всем моделям:
	/// - overview по моделям;
	/// - топ-фичи per-model;
	/// - общая таблица по всем фичам (без отсечения по top N), с reason для слабых/нулевых.
	/// </summary>
	internal static class FeatureImportanceSummary
		{
		private sealed class GlobalFeatureAgg
			{
			public string Name { get; set; } = string.Empty;

			/// <summary>Сколько моделей вообще видели эту фичу.</summary>
			public int ModelsTotal;

			/// <summary>Сколько моделей дали ненулевой вклад (ImportanceAuc &gt; 0).</summary>
			public int ModelsNonZero;

			/// <summary>Сумма ImportanceAuc по моделям с ненулевым вкладом.</summary>
			public double ImpSum;

			/// <summary>Максимальный вклад по моделям.</summary>
			public double ImpMax;

			/// <summary>Сумма corr_s по моделям.</summary>
			public double CorrScoreSum;

			/// <summary>Сколько раз corr_s был валиден (не NaN).</summary>
			public int CorrScoreCount;

			/// <summary>Сколько моделей показали DeltaMean &gt; 0.</summary>
			public int SignPos;

			/// <summary>Сколько моделей показали DeltaMean &lt; 0.</summary>
			public int SignNeg;

			/// <summary>Общий support: сумма (pos+neg) по всем моделям.</summary>
			public int SupportTotal;
			}

		/// <summary>
		/// Печать сводки:
		/// topPerModel — сколько фич показывать в блоке "Top features per model".
		/// topGlobalFeatures сейчас игнорируется — в глобальной таблице печатаем все фичи.
		/// importanceThreshold — порог, который влияет только на reason/цвета, но не на фильтрацию.
		/// </summary>
		public static void PrintGlobalSummary (
			int topPerModel,
			int topGlobalFeatures,
			double importanceThreshold )
			{
			var snaps = FeatureImportanceSnapshots.GetSnapshots ();
			if (snaps == null || snaps.Count == 0)
				{
				Console.WriteLine ("[pfi:summary] no snapshots registered.");
				return;
				}

			Console.WriteLine ();
			Console.WriteLine ($"[pfi:summary] models analyzed = {snaps.Count}");

			// ===== 1) Models overview =====

			Console.WriteLine ();
			Console.WriteLine ("-- Models overview --");
			Console.WriteLine (" model-tag                 AUC   rows   pos   neg");

			foreach (var s in snaps)
				{
				var stats = s.Stats;
				int pos = 0;
				int neg = 0;
				int rows = 0;

				if (stats != null && stats.Count > 0)
					{
					// CountPos/CountNeg одинаковы для всех фич в рамках одной модели.
					var first = stats[0];
					pos = first.CountPos;
					neg = first.CountNeg;
					rows = pos + neg;
					}

				Console.WriteLine (
					"{0,-24} {1,5:F3} {2,6} {3,5} {4,5}",
					TruncateTag (s.Tag, 24),
					s.BaselineAuc,
					rows,
					pos,
					neg);
				}

			// ===== 2) Top features per model (локальный top, как было) =====

			Console.WriteLine ();
			Console.WriteLine ("-- Top features per model --");

			foreach (var s in snaps)
				{
				var stats = s.Stats ?? Array.Empty<FeatureStats> ();
				if (stats.Count == 0)
					{
					Console.WriteLine (" {0,-24} (no stats)", TruncateTag (s.Tag, 24));
					continue;
					}

				Console.WriteLine (
					" {0,-24} feature                imp(AUC)   d(1-0)  corr_s",
					TruncateTag (s.Tag, 24));

				int printed = 0;

				foreach (var fs in stats
					.OrderByDescending (x => x.ImportanceAuc))
					{
					if (printed >= topPerModel)
						break;

					// Самую важную печатаем всегда, остальные можно чуть фильтровать.
					if (fs.ImportanceAuc <= 0 && printed > 0)
						continue;

					printed++;

					Console.WriteLine (
						"                         {0,-22} {1,8:F4} {2,8:F4} {3,7:F3}",
						FeatureImportanceCore.TruncateName (fs.Name, 22),
						fs.ImportanceAuc,
						fs.DeltaMean,
						fs.CorrScore);
					}

				Console.WriteLine ();
				}

			// ===== 3) Глобальная агрегация по всем фичам (без обрезания по top N) =====

			var agg = new Dictionary<string, GlobalFeatureAgg> (StringComparer.Ordinal);

			foreach (var s in snaps)
				{
				var stats = s.Stats ?? Array.Empty<FeatureStats> ();

				foreach (var fs in stats)
					{
					if (!agg.TryGetValue (fs.Name, out var g))
						{
						g = new GlobalFeatureAgg { Name = fs.Name };
						agg[fs.Name] = g;
						}

					g.ModelsTotal++;

					var support = fs.CountPos + fs.CountNeg;
					g.SupportTotal += support;

					if (fs.ImportanceAuc > 0)
						{
						g.ModelsNonZero++;
						g.ImpSum += fs.ImportanceAuc;
						if (fs.ImportanceAuc > g.ImpMax)
							g.ImpMax = fs.ImportanceAuc;
						}

					if (!double.IsNaN (fs.CorrScore))
						{
						g.CorrScoreSum += fs.CorrScore;
						g.CorrScoreCount++;
						}

					if (fs.DeltaMean > 0) g.SignPos++;
					else if (fs.DeltaMean < 0) g.SignNeg++;
					}
				}

			// Никакого отсечения — просто сортируем по средней важности.
			var aggList = agg.Values
				.OrderByDescending (a =>
					a.ModelsNonZero > 0 ? a.ImpSum / a.ModelsNonZero : 0.0)
				.ToList ();

			Console.WriteLine ();
			Console.WriteLine ("-- Global features across models (all) --");
			Console.WriteLine ("  #  feature               nz/total  imp_avg% imp_max%  sign  corr_s_avg        reason");

			int rank = 1;
			int maxToShow = aggList.Count; // Печатаем всё, игнорируя topGlobalFeatures

			for (int i = 0; i < maxToShow; i++)
				{
				var a = aggList[i];

				double impAvg = (a.ModelsNonZero > 0)
					? a.ImpSum / a.ModelsNonZero
					: 0.0;

				double impAvgPct = impAvg * 100.0;   // drop AUC в p.p.
				double impMaxPct = a.ImpMax * 100.0; // максимум drop AUC в p.p.

				double corrAvg = (a.CorrScoreCount > 0)
					? a.CorrScoreSum / a.CorrScoreCount
					: 0.0;

				string signStr;
				if (a.SignPos > 0 && a.SignNeg == 0) signStr = "++";
				else if (a.SignNeg > 0 && a.SignPos == 0) signStr = "--";
				else if (a.SignPos > 0 && a.SignNeg > 0) signStr = "+/-";
				else signStr = "0";

				string reason = BuildReasonForFeature (a, importanceThreshold);

				// Цвет по силе вклада (по средней важности в p.p.)
				var oldColor = Console.ForegroundColor;
				var color = ConsoleColor.DarkGray;

				double thrPct = importanceThreshold * 100.0;

				if (impAvgPct >= thrPct * 3)       // сильно выше порога
					color = ConsoleColor.Green;
				else if (impAvgPct >= thrPct)      // просто выше порога
					color = ConsoleColor.Yellow;

				Console.ForegroundColor = color;

				Console.WriteLine (
					"{0,3} {1,-20} {2,2}/{3,-3}   {4,7:F2} {5,8:F2}  {6,-4} {7,10:F3}  {8}",
					rank,
					FeatureImportanceCore.TruncateName (a.Name, 20),
					a.ModelsNonZero,
					a.ModelsTotal,
					impAvgPct,
					impMaxPct,
					signStr,
					corrAvg,
					reason);

				Console.ForegroundColor = oldColor;
				rank++;
				}

			Console.WriteLine ();
			}

		private static string TruncateTag ( string tag, int maxLen )
			{
			if (string.IsNullOrEmpty (tag) || tag.Length <= maxLen)
				return tag;

			return tag.Substring (0, maxLen - 1) + "…";
			}

		/// <summary>
		/// Эвристика "почему вклад нулевой/слабый".
		/// Работает на аггрегированных цифрах, поэтому даёт только грубое объяснение.
		/// </summary>
		private static string BuildReasonForFeature ( GlobalFeatureAgg a, double importanceThreshold )
			{
			double impAvg = (a.ModelsNonZero > 0)
				? a.ImpSum / a.ModelsNonZero
				: 0.0;

			// Средний размер выборки на модель (очень грубо).
			double avgSupportPerModel = (a.ModelsTotal > 0)
				? (double) a.SupportTotal / a.ModelsTotal
				: 0.0;

			if (a.ModelsTotal == 0)
				return "нет моделей";

			// Фича никогда не дала вклад (PFI==0 везде).
			if (a.ModelsNonZero == 0)
				{
				if (avgSupportPerModel < 30)
					return "мало примеров / модель не успела увидеть паттерн";

				return "модель почти не использует фичу (PFI≈0 во всех моделях)";
				}

			// Есть вклад, но ниже порога.
			if (impAvg < importanceThreshold)
				{
				if (avgSupportPerModel < 30)
					return "слабый вклад, мало примеров";

				return "слабый вклад (PFI чуть выше нуля)";
				}

			// Вклад уверенно выше порога.
			return "значимый вклад";
			}
		}
	}
