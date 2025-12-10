using System;
using System.Collections.Generic;
using System.Linq;
using SolSignalModel1D_Backtest.Core.Data;

namespace SolSignalModel1D_Backtest.Core.ML.Diagnostics.PnL
	{
	/// <summary>
	/// Простейший PnL-пробник по PredictionRecord:
	/// - делит записи на train/OOS по trainUntilUtc;
	/// - строит "наивный" PnL без плеча, SL, delayed и Anti-D;
	/// - печатает метрики отдельно для train и OOS.
	/// Используется только для диагностики возможной утечки.
	/// </summary>
	public static class DailyPnlProbe
		{
		/// <summary>
		/// Запускает простой PnL-анализ по дневным PredictionRecord.
		/// Данные делятся по trainUntilUtc, направление берётся из PredLabel + микро.
		/// </summary>
		public static void RunSimpleProbe (
			IReadOnlyList<PredictionRecord> records,
			DateTime trainUntilUtc )
			{
			if (records == null || records.Count == 0)
				{
				Console.WriteLine ("[pnl-probe] records is null or empty – nothing to compute.");
				return;
				}

			// Сначала сортируем по дате, чтобы эквити считалось в правильном порядке.
			var ordered = records
				.OrderBy (r => r.DateUtc)
				.ToList ();

			var train = ordered
				.Where (r => r.DateUtc <= trainUntilUtc)
				.ToList ();

			var oos = ordered
				.Where (r => r.DateUtc > trainUntilUtc)
				.ToList ();

			Console.WriteLine (
				$"[pnl-probe] trainUntilUtc = {trainUntilUtc:yyyy-MM-dd}, " +
				$"totalRecords = {ordered.Count}, train = {train.Count}, oos = {oos.Count}");

			if (train.Count == 0 || oos.Count == 0)
				{
				Console.WriteLine ("[pnl-probe] WARNING: one of splits (train/OOS) is empty – results may be uninformative.");
				}

			// Считаем PnL для train и OOS отдельно.
			var trainStats = ComputeSimplePnlStats (train);
			var oosStats = ComputeSimplePnlStats (oos);

			PrintStats ("[pnl-probe] TRAIN", trainStats);
			PrintStats ("[pnl-probe] OOS  ", oosStats);
			}

		/// <summary>
		/// Вычисляет наивный PnL по списку PredictionRecord:
		/// - направление сделки определяется PredLabel + микро;
		/// - доходность = (Close24 - Entry) / Entry (для long), с минусом для short;
		/// - эквити считается как последовательное умножение (1 + ret);
		/// - считается суммарный PnL, win-rate, max DD, среднее и std.
		/// </summary>
		private static SimplePnlStats ComputeSimplePnlStats ( IReadOnlyList<PredictionRecord> records )
			{
			if (records == null || records.Count == 0)
				{
				return SimplePnlStats.Empty;
				}

			int trades = 0;
			int wins = 0;

			var returns = new List<double> (records.Count);

			double equity = 1.0;
			double peakEquity = 1.0;
			double maxDrawdown = 0.0; // отрицательное значение (процент падения от пика)

			foreach (var rec in records)
				{
				// Направление по дневной модели с учётом микро-слоя.
				bool goLong =
					rec.PredLabel == 2 ||
					(rec.PredLabel == 1 && rec.PredMicroUp);

				bool goShort =
					rec.PredLabel == 0 ||
					(rec.PredLabel == 1 && rec.PredMicroDown);

				// Если нет торгового сигнала — день пропускается.
				if (!goLong && !goShort)
					{
					continue;
					}

				if (rec.Entry <= 0.0 || rec.Close24 <= 0.0)
					{
					// Некорректные цены, пропускаем день, чтобы не ломать статистику.
					Console.WriteLine (
						$"[pnl-probe] skip {rec.DateUtc:yyyy-MM-dd}: invalid prices Entry={rec.Entry}, Close24={rec.Close24}");
					continue;
					}

				// Дневная доходность без плеча.
				double dayRet = (rec.Close24 - rec.Entry) / rec.Entry;

				// Для short инвертируем знак.
				if (goShort && !goLong)
					{
					dayRet = -dayRet;
					}
				else if (goLong && goShort)
					{
					// Теоретически не должно происходить. На всякий случай логируем и пропускаем.
					Console.WriteLine (
						$"[pnl-probe] ambiguous direction on {rec.DateUtc:yyyy-MM-dd}, " +
						$"PredLabel={rec.PredLabel}, PredMicroUp={rec.PredMicroUp}, PredMicroDown={rec.PredMicroDown} – skip.");
					continue;
					}

				trades++;
				returns.Add (dayRet);

				if (dayRet > 0)
					{
					wins++;
					}

				// Обновляем эквити и максимум.
				equity *= (1.0 + dayRet);

				if (equity > peakEquity)
					{
					peakEquity = equity;
					}

				double dd = equity / peakEquity - 1.0;
				if (dd < maxDrawdown)
					{
					maxDrawdown = dd;
					}
				}

			if (trades == 0 || returns.Count == 0)
				{
				return SimplePnlStats.Empty;
				}

			double totalRet = equity - 1.0; // в долях
			double winRate = (double) wins / trades;

			// Среднее и стандартное отклонение по дневным доходностям.
			double mean = returns.Average ();
			double variance = returns
				.Select (r => (r - mean) * (r - mean))
				.DefaultIfEmpty (0.0)
				.Average ();

			double std = Math.Sqrt (variance);

			return new SimplePnlStats (
				Trades: trades,
				TotalReturn: totalRet,
				WinRate: winRate,
				MaxDrawdown: maxDrawdown,
				MeanReturn: mean,
				StdReturn: std);
			}

		/// <summary>
		/// Печатает сводку по PnL в консоль.
		/// Все проценты выводятся в человекочитаемом виде.
		/// </summary>
		private static void PrintStats ( string prefix, SimplePnlStats stats )
			{
			if (stats.Trades == 0)
				{
				Console.WriteLine ($"{prefix}: no trades.");
				return;
				}

			Console.WriteLine (
				$"{prefix}: trades={stats.Trades}, " +
				$"totalPnL={stats.TotalReturn * 100.0:0.00} %, " +
				$"winRate={stats.WinRate * 100.0:0.0} %, " +
				$"maxDD={stats.MaxDrawdown * 100.0:0.0} %, " +
				$"mean={stats.MeanReturn * 100.0:0.00} %, " +
				$"std={stats.StdReturn * 100.0:0.00} %");
			}

		/// <summary>
		/// Небольшой контейнер с метриками PnL-пробника.
		/// Используется только внутри диагностического кода.
		/// </summary>
		private readonly record struct SimplePnlStats (
			int Trades,
			double TotalReturn,
			double WinRate,
			double MaxDrawdown,
			double MeanReturn,
			double StdReturn )
			{
			public static readonly SimplePnlStats Empty = new (
				Trades: 0,
				TotalReturn: 0.0,
				WinRate: 0.0,
				MaxDrawdown: 0.0,
				MeanReturn: 0.0,
				StdReturn: 0.0);
			}
		}
	}
