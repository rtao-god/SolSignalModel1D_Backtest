using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.ML;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Backtest
	{
	public sealed class BacktestRunner
		{
		public async Task RunAsync ()
			{
			Console.OutputEncoding = System.Text.Encoding.UTF8;
			CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

			var http = HttpFactory.CreateDefault ("SolSignalModel1D_Backtest/1.0");

			var sol6h = await DataLoading.GetBinance6h (http, "SOLUSDT", 6000);
			var btc6h = await DataLoading.GetBinance6h (http, "BTCUSDT", 6000);
			var paxg6h = await DataLoading.GetBinance6h (http, "PAXGUSDT", 6000, allowNull: true);
			var sol1h = await DataLoading.GetBinance1h (http, "SOLUSDT", 8000, allowNull: true);

			if (sol6h.Count == 0 || btc6h.Count == 0)
				{
				Console.WriteLine ("[fatal] no candles");
				return;
				}

			var sol6hDict = sol6h.ToDictionary (c => c.OpenTimeUtc, c => c);
			var nyTz = TimeZones.GetNewYork ();

			var solTrainWindows = Windowing.FilterNyTrainWindows (sol6h, nyTz);
			var btcTrainWindows = Windowing.FilterNyTrainWindows (btc6h, nyTz);
			var paxgTrainWindows = paxg6h != null
				? Windowing.FilterNyTrainWindows (paxg6h, nyTz)
				: new List<Candle6h> ();

			var fngHistory = await DataLoading.GetFngHistory (http);
			DateTime oldest = solTrainWindows.First ().OpenTimeUtc.Date.AddDays (-45);
			DateTime newest = solTrainWindows.Last ().OpenTimeUtc.Date;
			var dxySeries = await DataLoading.GetDxySeries (http, oldest, newest);
			var extraDaily = DataLoading.TryLoadExtraDaily ("extra.json");

			var rows = RowBuilder.BuildRowsDaily (
				solTrainWindows,
				btcTrainWindows,
				paxgTrainWindows,
				sol6h,
				fngHistory,
				dxySeries,
				extraDaily,
				nyTz
			).OrderBy (r => r.Date).ToList ();

			foreach (var r in rows)
				{
				if (r.Features == null)
					{
					r.Features = new double[MlSchema.FeatureCount];
					}
				else if (r.Features.Length != MlSchema.FeatureCount)
					{
					var arr = new double[MlSchema.FeatureCount];
					Array.Copy (r.Features, arr, Math.Min (r.Features.Length, MlSchema.FeatureCount));
					r.Features = arr;
					}
				}

			var slOffline = SlOfflineBuilder.Build (rows, sol1h, sol6hDict);

			var pullbackOffline = PullbackContinuationOfflineBuilder.Build (rows, sol1h, sol6hDict);
			var smallOffline = SmallImprovementOfflineBuilder.Build (rows, sol1h, sol6hDict);

			var loop = new RollingLoop ();
			await loop.RunAsync (rows, sol1h, sol6hDict, slOffline, pullbackOffline, smallOffline);
			}
		}
	}
