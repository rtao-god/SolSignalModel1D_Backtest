namespace SolSignalModel1D_Backtest.Core.Causal.ML.Shared
	{
    /// <summary>
    /// Имена признаков для дневной / микро-модели.
    /// Индекс строго соответствует порядку добавления фич в RowBuilder.Causal.Features.
    /// </summary>
    public static class DailyFeatureSchema
    {
        public const int UsedCount = MlSchema.FeatureCount;

        public static readonly string[] Names = Build();

        private static string[] Build()
        {
            // Инвариант: имена и порядок обязаны 1:1 совпадать с CausalDataRow.FeaturesVector.
            var names = CausalDataRow.FeatureNames.ToArray();

            if (names.Length != MlSchema.FeatureCount)
            {
                throw new InvalidOperationException(
                    $"[DailyFeatureSchema] mismatch: names={names.Length}, MlSchema.FeatureCount={MlSchema.FeatureCount}.");
            }

            return names;
        }
    }

    /// <summary>
    /// Имена признаков для SL-модели (SlOfflineBuilder/SlFeatureBuilder).
    /// SL-вектор фиксированно 11-мерный.
    /// </summary>
    public static class SlFeatureSchema
		{
		public const int UsedCount = 11;

		/// <summary>
		/// Имена SL-фич длиной ровно UsedCount.
		/// Индексы должны совпадать с порядком записи в SlFeatureBuilder.
		/// </summary>
		public static readonly string[] Names = Build ();

		private static string[] Build ()
			{
			var names = new string[UsedCount];

			names[0] = "GoLongFlag";
			names[1] = "StrongSignalFlag";
			names[2] = "DayMinMove";
			names[3] = "Range6hTotal";
			names[4] = "Range2hEarly";
			names[5] = "Range2hLast";
			names[6] = "DistToHigh6h";
			names[7] = "DistToLow6h";
			names[8] = "Last1hWickiness";
			names[9] = "EntryHourNorm";
			names[10] = "DayMinMoveHighFlag";

			return names;
			}
		}

	public static class MicroFeatureSchema
		{
		public static string[] Names => DailyFeatureSchema.Names;
		}
	}
