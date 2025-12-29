namespace SolSignalModel1D_Backtest.Core.Causal.Analytics.Contracts
{
    /// <summary>
    /// Канонические причины отсутствия данных в диагностике.
    /// </summary>
    public static class MissingReasonCodes
    {
        public const string NotEvaluated = "NotEvaluated";
        public const string NoSignalDay = "NoSignalDay";
        public const string No1mPath = "No1mPath";
        public const string No1hFeatures = "No1hFeatures";
        public const string Weekend = "Weekend";
        public const string ModelDisabled = "ModelDisabled";
        public const string TooFewRows = "TooFewRows";
        public const string NonFlatTruth = "NonFlatTruth";
        public const string MicroNeutral = "MicroNeutral";
        public const string MissingTruth = "MissingTruth";
        public const string MicroNoTruth = "MicroNoTruth";
        public const string MicroNoPredictions = "MicroNoPredictions";
        public const string MicroNoEvaluated = "MicroNoEvaluated";
        public const string MicroNoUpPred = "MicroNoUpPred";
        public const string MicroNoDownPred = "MicroNoDownPred";
        public const string MicroNoNonFlatDirection = "MicroNoNonFlatDirection";
        public const string SlNoSignalDays = "SlNoSignalDays";
        public const string SlNoScoredDays = "SlNoScoredDays";
        public const string SlNoOutcomeDays = "SlNoOutcomeDays";
        public const string SlNoSlDays = "SlNoSlDays";
        public const string SlNoTpDays = "SlNoTpDays";
        public const string SlPrAucNotEnoughPoints = "SlPrAucNotEnoughPoints";
        public const string SlPrAucNoPos = "SlPrAucNoPos";
        public const string SlPrAucNoNeg = "SlPrAucNoNeg";
    }
}
