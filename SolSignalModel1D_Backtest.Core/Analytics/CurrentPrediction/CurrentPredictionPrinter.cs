using System;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Utils;

namespace SolSignalModel1D_Backtest.Core.Analytics.CurrentPrediction
	{
	/// <summary>
	/// Консольный вывод "текущего прогноза":
	/// - берёт готовый CurrentPredictionSnapshot;
	/// - показывает даты, режим, SL-prob;
	/// - рисует табличку по политикам.
	/// Внутри только форматирование, без новой математики.
	/// </summary>
	public static class CurrentPredictionPrinter
		{
		private static readonly TimeZoneInfo NyTz = TimeZones.NewYork;

		public static void Print ( CurrentPredictionSnapshot snapshot )
			{
			if (snapshot == null)
				throw new ArgumentNullException (
					nameof (snapshot),
					"[current] CurrentPredictionSnapshot == null в CurrentPredictionPrinter.Print — нарушен инвариант пайплайна"
				);

			var tbTz = TimeZoneInfo.FindSystemTimeZoneById ("Asia/Tbilisi");
			DateTime nyTime = TimeZoneInfo.ConvertTimeFromUtc (snapshot.PredictionDateUtc, NyTz);
			DateTime tbNow = TimeZoneInfo.ConvertTimeFromUtc (DateTime.UtcNow, tbTz);

			ConsoleStyler.WriteHeader ("=== ТЕКУЩИЙ ПРОГНОЗ ===");
			Console.WriteLine ($"Дата прогноза (NY): {nyTime:yyyy-MM-dd HH:mm}");
			Console.WriteLine ($"Текущее время (Tbilisi): {tbNow:yyyy-MM-dd HH:mm}");
			Console.WriteLine ($"Predicted class: {snapshot.PredLabelDisplay}");
			Console.WriteLine ($"Micro: {snapshot.MicroDisplay}");
			Console.WriteLine ($"Regime: {(snapshot.RegimeDown ? "DOWN" : "NORMAL")}");
			Console.WriteLine ($"SL-prob: {snapshot.SlProb:0.00} → SlHighDecision={snapshot.SlHighDecision}");
			Console.WriteLine ($"Entry: {snapshot.Entry:0.0000} USDT");
			Console.WriteLine ();

			if (snapshot.Forward24h != null)
				{
				var fwd = snapshot.Forward24h;
				Console.WriteLine ("=== Forward 24h (baseline) ===");
				Console.WriteLine ($"MaxHigh: {fwd.MaxHigh:0.0000} USDT");
				Console.WriteLine ($"MinLow:  {fwd.MinLow:0.0000} USDT");
				Console.WriteLine ($"Close:   {fwd.Close:0.0000} USDT");
				Console.WriteLine ();
				}

			// Текстовый комментарий модели (если есть)
			if (!string.IsNullOrWhiteSpace (snapshot.Reason))
				{
				Console.WriteLine ("Комментарий модели:");
				Console.WriteLine (snapshot.Reason);
				Console.WriteLine ();
				}

			// Объяснение прогноза: top факторов/источников
			if (snapshot.ExplanationItems.Count > 0)
				{
				Console.WriteLine ("=== Объяснение прогноза (top факторов) ===");

				var ordered = snapshot.ExplanationItems
					.OrderBy (e => e.Rank == 0 ? int.MaxValue : e.Rank)
					.ThenBy (e => e.Name);

				foreach (var item in ordered)
					{
					var valuePart = item.Value.HasValue
						? $" (value={item.Value.Value:0.####})"
						: string.Empty;

					Console.WriteLine ($"[{item.Kind}] {item.Name}: {item.Description}{valuePart}");
					}

				Console.WriteLine ();
				}

			var table = new TextTable ();
			table.AddHeader (
				"Policy",
				"Branch",
				"RiskDay",
				"HasDirection",
				"Skipped",
				"Direction",
				"Leverage",
				"Entry",
				"SL%",
				"TP%",
				"SL price",
				"TP price",
				"Position $",
				"Position qty",
				"Liq price",
				"Liq dist %");

			foreach (var row in snapshot.PolicyRows)
				{
				string F ( double? v, string fmt ) => v.HasValue ? v.Value.ToString (fmt) : "-";

				table.AddRow (new[]
					{
					row.PolicyName,
					row.Branch,
					row.IsRiskDay.ToString (),
					row.HasDirection.ToString (),
					row.Skipped.ToString (),
					row.Direction,
					row.Leverage.ToString ("0.##"),
					row.Entry.ToString ("0.0000"),
					row.SlPct.HasValue ? row.SlPct.Value.ToString ("0.0") : "-",
					row.TpPct.HasValue ? row.TpPct.Value.ToString ("0.0") : "-",
					F (row.SlPrice, "0.0000"),
					F (row.TpPrice, "0.0000"),
					F (row.PositionUsd, "0.00"),
					F (row.PositionQty, "0.000"),
					F (row.LiqPrice, "0.0000"),
					F (row.LiqDistPct, "0.0")
					});
				}

			table.WriteToConsole ();
			Console.WriteLine ();
			}
		}
	}
