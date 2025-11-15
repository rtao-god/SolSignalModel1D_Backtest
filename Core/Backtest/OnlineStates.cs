using System;
using System.Collections.Generic;
using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.ML.Delayed;
using SolSignalModel1D_Backtest.Core.ML.Delayed.Trainers;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	/// <summary>
	/// Онлайн-состояние SL-модели: каузально доучиваемся на оффлайн-сделках.
	/// </summary>
	public sealed class SlOnlineState
		{
		public SlFirstTrainer? Trainer { get; set; }
		public ITransformer? Model { get; set; }
		public PredictionEngine<SlHitSample, SlHitPrediction>? Engine { get; set; }

		public int MinTrainSamples { get; set; } = 80;
		public int RetrainEvery { get; set; } = 30;
		public int SamplesAtLastTrain { get; set; } = 0;

		// ВАЖНО: дефолтный порог, чтобы DayExecutor не считал все дни опасными
		public float SLRiskThreshold { get; set; } = 0.55f;

		public void TryRetrain ( List<SlHitSample> pastSamples, DateTime asOf )
			{
			if (Trainer == null) return;
			if (pastSamples.Count < MinTrainSamples) return;

			if (Model == null || pastSamples.Count - SamplesAtLastTrain >= RetrainEvery)
				{
				Model = Trainer.Train (pastSamples, asOf);
				Engine = Trainer.CreateEngine (Model);
				SamplesAtLastTrain = pastSamples.Count;
				}
			}
		}

	/// <summary>
	/// Онлайн-состояние target-level / delayed-модели (базовое).
	/// </summary>
	public sealed class TargetLevelOnlineState
		{
		public TargetLevelTrainer? Trainer { get; set; }
		public ITransformer? Model { get; set; }
		public PredictionEngine<TargetLevelSample, TargetLevelPrediction>? Engine { get; set; }

		public int MinTrainSamples { get; set; } = 80;
		public int RetrainEvery { get; set; } = 30;
		public int SamplesAtLastTrain { get; set; } = 0;

		public void TryRetrain ( List<TargetLevelSample> pastSamples, DateTime asOf )
			{
			if (Trainer == null) return;
			if (pastSamples.Count < MinTrainSamples) return;

			if (Model == null || pastSamples.Count - SamplesAtLastTrain >= RetrainEvery)
				{
				Model = Trainer.Train (pastSamples, asOf);
				Engine = Trainer.CreateEngine (Model);
				SamplesAtLastTrain = pastSamples.Count;
				}
			}
		}
	}
