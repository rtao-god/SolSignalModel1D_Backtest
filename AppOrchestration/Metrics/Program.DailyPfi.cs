using SolSignalModel1D_Backtest.Core.ML.Daily;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Utils;
using DataRow = SolSignalModel1D_Backtest.Core.Causal.Data.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		/// <summary>
		/// Считает PFI по дневным моделям:
		/// - train-окно до _trainUntilUtc;
		/// - при наличии OOS-хвоста — PFI по нему с отдельным тегом.
		/// </summary>
		private static void RunDailyPfi ( List<DataRow> allRows )
			{
			// Раньше здесь было два прохода по allRows через Where(...<=)... и Where(...>...).
			// Теперь всё делается за один проход в хелпере, что уменьшает время на больших выборках.
			var (dailyTrainRows, dailyOosRows) = TrainOosSplitHelper.SplitByTrainBoundary (allRows, _trainUntilUtc);

			if (dailyTrainRows.Count < 50)
				{
				Console.WriteLine ($"[pfi:daily] not enough train rows for PFI (count={dailyTrainRows.Count}), skip.");
				return;
				}

			var dailyTrainer = new ModelTrainer ();

			// Обучение всех дневных моделей на train-окне.
			var bundle = dailyTrainer.TrainAll (dailyTrainRows, datesToExclude: null);

			// PFI по train-сетам.
			DailyModelDiagnostics.LogFeatureImportanceOnDailyModels (bundle, dailyTrainRows, "train");

			// При наличии OOS-хвоста — отдельный PFI по нему.
			if (dailyOosRows.Count > 0)
				{
				var tag = dailyOosRows.Count >= 50 ? "oos" : "oos-small";
				DailyModelDiagnostics.LogFeatureImportanceOnDailyModels (bundle, dailyOosRows, tag);
				}
			else
				{
				Console.WriteLine ("[pfi:daily] no OOS rows after _trainUntilUtc, skip oos PFI.");
				}
			}
		}
	}
