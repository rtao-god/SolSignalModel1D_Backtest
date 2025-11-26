using Microsoft.ML;

namespace SolSignalModel1D_Backtest.Core.Analytics.ML
	{
	/// <summary>
	/// Высокоуровневый фасад над ядром PFI:
	/// - LogBinaryFeatureImportance: считает PFI, печатает табличку, сохраняет снапшот;
	/// - AnalyzeBinaryFeatureImportance: только считает и возвращает stats (без вывода);
	/// - PrintGlobalSummary: печатает сводку по всем ранее зарегистрированным моделям.
	/// </summary>
	public static partial class FeatureImportanceAnalyzer
		{
		/// <summary>
		/// Стандартный вход для кода раннера:
		/// считает PFI + direction, печатает табличку и регистрирует снапшот
		/// для глобального summary.
		/// </summary>
		public static void LogBinaryFeatureImportance (
			MLContext ml,
			ITransformer model,
			IDataView data,
			string[] featureNames,
			string tag,
			string labelColumnName = "Label",
			string featuresColumnName = "Features" )
			{
			if (ml == null) throw new ArgumentNullException (nameof (ml));
			if (model == null) throw new ArgumentNullException (nameof (model));
			if (data == null) throw new ArgumentNullException (nameof (data));
			if (featureNames == null) throw new ArgumentNullException (nameof (featureNames));

			// 1) Считаем PFI + direction без сайд-эффектов.
			var stats = FeatureImportanceCore.AnalyzeBinaryFeatureImportance (
				ml,
				model,
				data,
				featureNames,
				tag,
				out var baselineAuc,
				labelColumnName,
				featuresColumnName);

			// 2) Регистрируем снапшот для последующей глобальной сводки.
			FeatureImportanceSnapshots.RegisterSnapshot (tag, baselineAuc, stats);

			// 3) Печатаем подробную табличку по этой модели.
			FeatureImportancePrinter.PrintTable (tag, baselineAuc, stats);
			}

		/// <summary>
		/// Чистый API: только считает PFI и возвращает FeatureStats.
		/// Никакого вывода в консоль и никаких snapshot'ов.
		/// Можно использовать для кастомных отчётов.
		/// </summary>
		public static List<FeatureStats> AnalyzeBinaryFeatureImportance (
			MLContext ml,
			ITransformer model,
			IDataView data,
			string[] featureNames,
			string tag,
			out double baselineAuc,
			string labelColumnName = "Label",
			string featuresColumnName = "Features" )
			{
			if (ml == null) throw new ArgumentNullException (nameof (ml));
			if (model == null) throw new ArgumentNullException (nameof (model));
			if (data == null) throw new ArgumentNullException (nameof (data));
			if (featureNames == null) throw new ArgumentNullException (nameof (featureNames));

			return FeatureImportanceCore.AnalyzeBinaryFeatureImportance (
				ml,
				model,
				data,
				featureNames,
				tag,
				out baselineAuc,
				labelColumnName,
				featuresColumnName);
			}

		/// <summary>
		/// Печатает агрегированную сводку по всем моделям,
		/// для которых ранее вызывался LogBinaryFeatureImportance(...).
		/// </summary>
		public static void PrintGlobalSummary (
			int topPerModel = 5,
			int topGlobalFeatures = int.MaxValue, // по умолчанию — без отсечения, печатаем все фичи
			double importanceThreshold = 0.003 )
			{
			FeatureImportanceSummary.PrintGlobalSummary (
				topPerModel,
				topGlobalFeatures,
				importanceThreshold);
			}
		}
	}
