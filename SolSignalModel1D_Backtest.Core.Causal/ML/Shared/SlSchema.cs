namespace SolSignalModel1D_Backtest.Core.Causal.ML.Shared
	{
	/// <summary>
	/// Схема фичей SL-слоя. Держим отдельно от общей MlSchema,
	/// потому что SL-модель использует другой вектор признаков (11), а не 64.
	/// </summary>
	public static class SlSchema
		{
		public const int FeatureCount = SlFeatureSchema.UsedCount;
		}
	}
