using Microsoft.ML;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.ML.Delayed.Trainers;
using System;
using System.Collections.Generic;

namespace SolSignalModel1D_Backtest.Core.ML.Delayed.States
	{
	public sealed class SmallImprovementOnlineState
		{
		public SmallImprovementTrainer? Trainer { get; set; }
		public int MinTrainSamples { get; set; } = 80;
		public int RetrainEvery { get; set; } = 30;

		public ITransformer? Model { get; private set; }
		public PredictionEngine<SmallImprovementSample, SlHitPrediction>? Engine { get; private set; }

		private int _lastTrainCount = 0;

		public void TryRetrain ( List<SmallImprovementSample> samples, DateTime asOfUtc )
			{
			if (Trainer == null) return;

			int past = samples.FindAll (s => s.EntryUtc < asOfUtc).Count;
			if (past < MinTrainSamples) return;

			if (Model == null || past - _lastTrainCount >= RetrainEvery)
				{
				var m = Trainer.Train (samples, asOfUtc);
				Model = m;
				Engine = Trainer.CreateEngine (m);
				_lastTrainCount = past;
				}
			}
		}
	}
