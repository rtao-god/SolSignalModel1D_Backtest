using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Causal.Data;
using SolSignalModel1D_Backtest.Core.Causal.ML.Micro;
using SolSignalModel1D_Backtest.Core.ML.Aggregation;
using SolSignalModel1D_Backtest.Core.ML.Shared;
using SolSignalModel1D_Backtest.Core.Time;
using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.Causal.ML.Shared
{
    /// <summary>
    /// Каузальный инференс-движок.
    /// Инварианты архитектуры:
    /// - вход ТОЛЬКО CausalDataRow (никаких BacktestRecord / Forward / Fact);
    /// - отсутствие модели не маскируется: либо явный debug-флаг, либо исключение;
    /// - вероятности обязаны быть конечными и в диапазоне [0..1], иначе это поломка пайплайна.
    /// Потокобезопасность:
    /// - Microsoft.ML.PredictionEngine не потокобезопасен; здесь используется общий lock.
    /// </summary>
    public sealed class PredictionEngine
    {
        private readonly ModelBundle _bundle;
        private readonly ProbabilityAggregationConfig _aggConfig;

        private readonly object _sync = new object();

        private PredictionEngine<MlSampleBinary, MlBinaryOutput>? _moveEng;
        private PredictionEngine<MlSampleBinary, MlBinaryOutput>? _dirEngNormal;
        private PredictionEngine<MlSampleBinary, MlBinaryOutput>? _dirEngDown;
        private PredictionEngine<MlSampleBinary, MlBinaryOutput>? _microEng;

        public static bool DebugAllowDisabledModels { get; set; } = false;
        public static bool DebugTreatMissingMoveAsFlat { get; set; } = false;
        public static bool DebugTreatMissingDirAsFlat { get; set; } = false;

        private const float FlatMicroProbThresh = 0.60f;

        private const int MicroDebugMaxRows = 10;
        private static int _microDebugPrinted;
        public static bool DebugPrintMicro { get; set; } = false;

        public readonly struct PredResult
        {
            public PredResult(int cls, string reason, MicroInfo micro, DailyProbabilities day, DailyProbabilities dayWithMicro)
            {
                Class = cls;
                Reason = reason;
                Micro = micro;
                Day = day;
                DayWithMicro = dayWithMicro;
            }

            public int Class { get; }
            public string Reason { get; }
            public MicroInfo Micro { get; }
            public DailyProbabilities Day { get; }
            public DailyProbabilities DayWithMicro { get; }
        }

        public PredictionEngine(ModelBundle bundle, ProbabilityAggregationConfig? aggregationConfig = null)
        {
            _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
            _aggConfig = aggregationConfig ?? new ProbabilityAggregationConfig();
        }

        public CausalPredictionRecord PredictCausal(CausalDataRow row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));

            var res = PredictInternal(row);
            return ToCausalRecord(row, res);
        }

        public List<CausalPredictionRecord> PredictManyCausal(IReadOnlyList<CausalDataRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var result = new List<CausalPredictionRecord>(rows.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i] ?? throw new InvalidOperationException("[PredictionEngine] rows contains null CausalDataRow item.");
                var res = PredictInternal(r);
                result.Add(ToCausalRecord(r, res));
            }

            return result;
        }

        public PredResult Predict(CausalDataRow row) => PredictInternal(row);

        private PredResult PredictInternal(CausalDataRow row)
        {
            try
            {
                if (_bundle.MlCtx == null)
                    throw new InvalidOperationException("[PredictionEngine] ModelBundle.MlCtx == null");

                var ml = _bundle.MlCtx;

                var dayKeyUtc = row.DayKeyUtc.Value;

                var fixedFeatures = ToFloatFixedFromVectorOrThrow(row.FeaturesVector, dayKeyUtc);

                var sample = new MlSampleBinary { Features = fixedFeatures };

                lock (_sync)
                {
                    // ===== 1) Move =====
                    MlBinaryOutput moveOut;

                    if (_bundle.MoveModel == null)
                    {
                        if (!DebugAllowDisabledModels)
                            throw new InvalidOperationException("[PredictionEngine] ModelBundle.MoveModel == null (нет move-модели)");

                        if (!DebugTreatMissingMoveAsFlat)
                        {
                            throw new InvalidOperationException(
                                "[PredictionEngine] MoveModel is missing. " +
                                "Enable DebugTreatMissingMoveAsFlat explicitly if you really want flat-fallback.");
                        }

                        var dayFlat = BuildPureFlatDayProbabilities();
                        var microInfoDbg = RunMicroIfAvailable(dayKeyUtc, fixedFeatures, ml);
                        var microProbsDbg = ConvertMicro(microInfoDbg);
                        var dayFlatMicroDbg = ProbabilityAggregator.ApplyMicroOverlay(dayFlat, microProbsDbg, _aggConfig);

                        string reasonDbg = microInfoDbg.Predicted
                            ? microInfoDbg.Up ? "day:flat+microUp(move-missing)" : "day:flat+microDown(move-missing)"
                            : "day:flat(move-missing)";

                        return new PredResult(1, reasonDbg, microInfoDbg, dayFlat, dayFlatMicroDbg);
                    }

                    var moveEng = GetOrCreateMoveEngineOrThrow(ml);
                    moveOut = moveEng.Predict(sample);

                    double pMove = moveOut.Probability;
                    ValidateProbabilityOrThrow(pMove, "[PredictionEngine] invalid move probability");

                    // ===== 2) Dir =====
                    var dirModel = row.RegimeDown && _bundle.DirModelDown != null
                        ? _bundle.DirModelDown
                        : _bundle.DirModelNormal;

                    if (dirModel == null)
                    {
                        if (!DebugAllowDisabledModels)
                            throw new InvalidOperationException("[PredictionEngine] DirModelNormal/DirModelDown == null (нет dir-модели)");

                        if (!DebugTreatMissingDirAsFlat)
                        {
                            throw new InvalidOperationException(
                                "[PredictionEngine] DirModel is missing. " +
                                "Enable DebugTreatMissingDirAsFlat explicitly if you really want flat-fallback.");
                        }

                        var dayFlat = BuildPureFlatDayProbabilities();
                        var microInfoDbg = RunMicroIfAvailable(dayKeyUtc, fixedFeatures, ml);
                        var microProbsDbg = ConvertMicro(microInfoDbg);
                        var dayFlatMicroDbg = ProbabilityAggregator.ApplyMicroOverlay(dayFlat, microProbsDbg, _aggConfig);

                        string reasonDbg = microInfoDbg.Predicted
                            ? microInfoDbg.Up ? "day:flat+microUp(dir-missing)" : "day:flat+microDown(dir-missing)"
                            : "day:flat(dir-missing)";

                        return new PredResult(1, reasonDbg, microInfoDbg, dayFlat, dayFlatMicroDbg);
                    }

                    var dirEng = GetOrCreateDirEngineOrThrow(ml, row.RegimeDown);
                    var dirOut = dirEng.Predict(sample);

                    double pUpGivenMove = dirOut.Probability;
                    ValidateProbabilityOrThrow(pUpGivenMove, "[PredictionEngine] invalid dir probability");

                    bool wantsUp = dirOut.PredictedLabel;

                    // ===== 3) BTC-фильтр (каузальный) =====
                    bool btcBlocksUp = false;
                    if (wantsUp)
                    {
                        bool btcEmaDown = row.BtcEma50vs200 < -0.002;
                        bool btcShortRed = row.BtcRet1 < 0 && row.BtcRet30 < 0;

                        if (btcEmaDown && btcShortRed)
                            btcBlocksUp = true;
                    }

                    // ===== 4) Day distribution через P(move) и P(up|move) =====
                    var rawDir = new DailyRawOutput
                    {
                        PMove = pMove,
                        PUpGivenMove = pUpGivenMove,
                        BtcFilterBlocksUp = btcBlocksUp,
                        BtcFilterBlocksFlat = false,
                        BtcFilterBlocksDown = false
                    };

                    var dayProbs = DayProbabilityBuilder.BuildDayProbabilities(rawDir);

                    // ===== 5) Flat-ветка: микро допускается только если move сказал "no-move" =====
                    if (!moveOut.PredictedLabel)
                    {
                        var microInfo = RunMicroIfAvailable(dayKeyUtc, fixedFeatures, ml);
                        var microProbs = ConvertMicro(microInfo);
                        var dayFlatMicro = ProbabilityAggregator.ApplyMicroOverlay(dayProbs, microProbs, _aggConfig);

                        string reason = microInfo.Predicted
                            ? microInfo.Up ? "day:flat+microUp" : "day:flat+microDown"
                            : "day:flat";

                        return new PredResult(1, reason, microInfo, dayProbs, dayFlatMicro);
                    }

                    // ===== 6) Move=true: микро не применяется =====
                    var microEmpty = new MicroInfo();
                    var microProbsEmpty = ConvertMicro(microEmpty);
                    var dayWithMicro = ProbabilityAggregator.ApplyMicroOverlay(dayProbs, microProbsEmpty, _aggConfig);

                    if (wantsUp)
                    {
                        if (btcBlocksUp)
                            return new PredResult(1, "day:move-up-blocked-by-btc", microEmpty, dayProbs, dayWithMicro);

                        return new PredResult(2, "day:move-up", microEmpty, dayProbs, dayWithMicro);
                    }

                    return new PredResult(0, "day:move-down", microEmpty, dayProbs, dayWithMicro);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PredictionEngine][ERROR] {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                throw;
            }
        }

        private static CausalPredictionRecord ToCausalRecord(CausalDataRow row, PredResult res)
        {
            var day = res.Day;
            var dayMicro = res.DayWithMicro;
            var micro = res.Micro;

            int predDay = ArgmaxClass(day);
            int predDayMicro = ArgmaxClass(dayMicro);

            double confMicro = 0.0;
            if (micro.Predicted)
                confMicro = Math.Max(micro.Prob, 1.0 - micro.Prob);

            return new CausalPredictionRecord
            {
                EntryUtc = new EntryUtc(row.EntryUtc.Value),

                FeaturesVector = row.FeaturesVector,

                RegimeDown = row.RegimeDown,
                MinMove = row.MinMove,
                Reason = res.Reason,

                PredLabel = res.Class,
                PredLabel_Day = predDay,
                PredLabel_DayMicro = predDayMicro,

                PredLabel_Total = res.Class,
                ProbUp_Total = dayMicro.PUp,
                ProbFlat_Total = dayMicro.PFlat,
                ProbDown_Total = dayMicro.PDown,

                ProbUp_Day = day.PUp,
                ProbFlat_Day = day.PFlat,
                ProbDown_Day = day.PDown,

                ProbUp_DayMicro = dayMicro.PUp,
                ProbFlat_DayMicro = dayMicro.PFlat,
                ProbDown_DayMicro = dayMicro.PDown,

                Conf_Day = Math.Max(day.PUp, Math.Max(day.PFlat, day.PDown)),
                Conf_Micro = confMicro,

                MicroPredicted = micro.Predicted,
                PredMicroUp = micro.Predicted && micro.Up,
                PredMicroDown = micro.Predicted && !micro.Up
            };
        }

        private static int ArgmaxClass(DailyProbabilities probs)
        {
            double best = probs.PDown;
            int label = 0;

            if (probs.PFlat > best) { best = probs.PFlat; label = 1; }
            if (probs.PUp > best) { label = 2; }

            return label;
        }

        private MicroInfo RunMicroIfAvailable(DateTime dayKeyUtc, float[] fixedFeatures, MLContext ml)
        {
            var microInfo = new MicroInfo();

            if (_bundle.MicroFlatModel == null)
                return microInfo;

            var microEng = GetOrCreateMicroEngineOrThrow(ml);

            var microSample = new MlSampleBinary { Features = fixedFeatures };
            var microOut = microEng.Predict(microSample);

            double pUp = microOut.Probability;
            ValidateProbabilityOrThrow(pUp, "[micro] invalid probability from model");

            double pDown = 1.0 - pUp;
            double confidence = Math.Max(pUp, pDown);
            bool accepted = confidence >= FlatMicroProbThresh;

            if (DebugPrintMicro && _microDebugPrinted < MicroDebugMaxRows)
            {
                Console.WriteLine(
                    "[debug-micro] {0:yyyy-MM-dd} pUp={1:0.000} pDown={2:0.000} conf={3:0.000} accepted={4}",
                    dayKeyUtc,
                    pUp,
                    pDown,
                    confidence,
                    accepted
                );

                _microDebugPrinted++;
            }

            microInfo.Predicted = accepted;
            microInfo.Up = accepted ? (pUp >= 0.5) : false;

            microInfo.ConsiderUp = accepted && (pUp >= 0.5);
            microInfo.ConsiderDown = accepted && (pUp < 0.5);

            microInfo.Prob = (float)pUp;

            return microInfo;
        }

        private static MicroProbabilities ConvertMicro(MicroInfo micro)
        {
            double pUp = micro.Prob;

            ValidateProbabilityOrThrow(pUp, "[micro] invalid probability value");

            if (!micro.Predicted)
            {
                return new MicroProbabilities
                {
                    HasPrediction = false,
                    PUpGivenFlat = double.NaN,
                    PDownGivenFlat = double.NaN,
                    Confidence = 0.0,
                    PredLabel = 1
                };
            }

            double pDown = 1.0 - pUp;

            return new MicroProbabilities
            {
                HasPrediction = true,
                PUpGivenFlat = pUp,
                PDownGivenFlat = pDown,
                Confidence = Math.Max(pUp, pDown),
                PredLabel = micro.Up ? 2 : 0
            };
        }

        private static DailyProbabilities BuildPureFlatDayProbabilities()
        {
            return new DailyProbabilities
            {
                PUp = 0.0,
                PFlat = 1.0,
                PDown = 0.0,
                Confidence = 1.0,
                BtcFilterBlockedUp = false,
                BtcFilterBlockedFlat = false,
                BtcFilterBlockedDown = false
            };
        }

        private static float[] ToFloatFixedFromVectorOrThrow(ReadOnlyMemory<double> vec, DateTime dayKeyUtc)
        {
            if (vec.IsEmpty)
                throw new InvalidOperationException($"[PredictionEngine] empty FeaturesVector for dayKey={dayKeyUtc:O}.");

            if (vec.Length != MlSchema.FeatureCount)
                throw new InvalidOperationException(
                    $"[PredictionEngine] FeaturesVector length mismatch for dayKey={dayKeyUtc:O}: got {vec.Length}, expected {MlSchema.FeatureCount}.");

            var span = vec.Span;
            var dst = new float[span.Length];

            for (int i = 0; i < span.Length; i++)
            {
                double x = span[i];

                if (!double.IsFinite(x))
                    throw new InvalidOperationException($"[PredictionEngine] non-finite feature at index={i} for dayKey={dayKeyUtc:O}: {x}.");

                dst[i] = (float)x;
            }

            return dst;
        }

        private static void ValidateProbabilityOrThrow(double p, string prefix)
        {
            if (!double.IsFinite(p) || p < 0.0 || p > 1.0)
                throw new InvalidOperationException($"{prefix}: {p}. Expected finite value in [0..1].");
        }

        private PredictionEngine<MlSampleBinary, MlBinaryOutput> GetOrCreateMoveEngineOrThrow(MLContext ml)
        {
            if (_bundle.MoveModel == null)
                throw new InvalidOperationException("[PredictionEngine] MoveModel is null");

            if (_moveEng != null) return _moveEng;

            _moveEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput>(_bundle.MoveModel);
            return _moveEng;
        }

        private PredictionEngine<MlSampleBinary, MlBinaryOutput> GetOrCreateDirEngineOrThrow(MLContext ml, bool regimeDown)
        {
            if (regimeDown && _bundle.DirModelDown != null)
            {
                if (_dirEngDown != null) return _dirEngDown;
                _dirEngDown = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput>(_bundle.DirModelDown);
                return _dirEngDown;
            }

            if (_bundle.DirModelNormal == null)
                throw new InvalidOperationException("[PredictionEngine] DirModelNormal is null");

            if (_dirEngNormal != null) return _dirEngNormal;

            _dirEngNormal = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput>(_bundle.DirModelNormal);
            return _dirEngNormal;
        }

        private PredictionEngine<MlSampleBinary, MlBinaryOutput> GetOrCreateMicroEngineOrThrow(MLContext ml)
        {
            if (_bundle.MicroFlatModel == null)
                throw new InvalidOperationException("[PredictionEngine] MicroFlatModel is null");

            if (_microEng != null) return _microEng;

            _microEng = ml.Model.CreatePredictionEngine<MlSampleBinary, MlBinaryOutput>(_bundle.MicroFlatModel);
            return _microEng;
        }
    }
}
