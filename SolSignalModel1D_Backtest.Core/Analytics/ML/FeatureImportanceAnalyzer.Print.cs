namespace SolSignalModel1D_Backtest.Core.Analytics.ML
	{
	/// <summary>
	/// Печать подробной таблички PFI + direction для одной модели.
	/// Вынесено отдельно, чтобы не раздувать основной фасад.
	/// </summary>
	internal static class FeatureImportancePrinter
		{
		internal static void PrintTable (
			string tag,
			double baselineAuc,
			List<FeatureStats> stats )
			{
			Console.WriteLine ();
			Console.WriteLine ($"===== PFI + direction ({tag}) =====");
			Console.WriteLine ($" baseline AUC = {baselineAuc:F4}");
			Console.WriteLine (" idx  feature                imp(AUC)   dAUC    mean[1]    mean[0]   d(1-0)  corr_y  corr_s   pos   neg");

			foreach (var s in stats)
				{
				var oldColor = Console.ForegroundColor;
				var color = ConsoleColor.Gray;

				// Простая подсветка:
				// - если ImportanceAuc маленькая → серый;
				// - если фича важная, цвет зависит от знака DeltaMean.
				if (s.ImportanceAuc >= 0.005)
					{
					if (s.DeltaMean > 0)
						color = ConsoleColor.Green;
					else if (s.DeltaMean < 0)
						color = ConsoleColor.Red;
					}

				Console.ForegroundColor = color;

				Console.WriteLine (
					"{0,4} {1,-22} {2,8:F4} {3,7:F4} {4,9:F4} {5,9:F4} {6,7:F4} {7,7:F3} {8,7:F3} {9,5} {10,5}",
					s.Index,
					FeatureImportanceCore.TruncateName (s.Name, 22),
					s.ImportanceAuc,
					s.DeltaAuc,
					s.MeanPos,
					s.MeanNeg,
					s.DeltaMean,
					s.CorrLabel,
					s.CorrScore,
					s.CountPos,
					s.CountNeg);

				Console.ForegroundColor = oldColor;
				}

			Console.WriteLine ();
			}
		}
	}
