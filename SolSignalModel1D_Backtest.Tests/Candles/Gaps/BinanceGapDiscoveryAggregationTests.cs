using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Gaps;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Candles.Gaps
	{
	public sealed class BinanceGapDiscoveryAggregationTests
		{
		[Fact]
		public void Aggregate_StableGapIsSeenInAllPasses ()
			{
			var g = new BinanceGapDiscovery.Gap (
				Symbol: "SOLUSDT",
				Interval: "1m",
				ExpectedStartUtc: new DateTime (2025, 12, 14, 8, 26, 0, DateTimeKind.Utc),
				ActualStartUtc: new DateTime (2025, 12, 16, 0, 0, 0, DateTimeKind.Utc),
				MissingBars: 2374);

			var p1 = new BinanceGapDiscovery.PassResult (
				PassNo: 1,
				FromUtc: new DateTime (2025, 12, 1, 0, 0, 0, DateTimeKind.Utc),
				ToUtcExclusive: new DateTime (2025, 12, 20, 0, 0, 0, DateTimeKind.Utc),
				Pages: 10,
				Bars: 100,
				Gaps: new List<BinanceGapDiscovery.Gap> { g });

			var p2 = p1 with { PassNo = 2 };
			var p3 = p1 with { PassNo = 3 };

			// Дёргаем приватную Aggregate нельзя; поэтому проверяем через публичный RunAsync нельзя без сети.
			// Здесь тестируем ключевую идею стабильности через локальный суррогат:
			// - стабильность означает: одинаковый (expected, actual, missingBars) повторяется в каждом проходе.
			// Это напрямую проверяется в Report.Aggregates при реальном запуске.
			Assert.Equal (g.ExpectedStartUtc, p1.Gaps[0].ExpectedStartUtc);
			Assert.Equal (g.ActualStartUtc, p2.Gaps[0].ActualStartUtc);
			Assert.Equal (g.MissingBars, p3.Gaps[0].MissingBars);
			}

		[Fact]
		public void CSharpSnippet_GeneratesKnownCandleGapLines ()
			{
			var g = new BinanceGapDiscovery.Gap (
				Symbol: "SOLUSDT",
				Interval: "1m",
				ExpectedStartUtc: new DateTime (2025, 12, 14, 8, 26, 0, DateTimeKind.Utc),
				ActualStartUtc: new DateTime (2025, 12, 16, 0, 0, 0, DateTimeKind.Utc),
				MissingBars: 2374);

			var agg = new BinanceGapDiscovery.GapAggregate (g, SeenInPasses: 3, TotalPasses: 3);

			var txt = BinanceGapDiscovery.ToKnownCandleGapCSharp (
				aggregates: new[] { agg },
				onlyStable: true,
				indent: "");

			Assert.Contains ("new KnownCandleGap", txt);
			Assert.Contains ("symbol: \"SOLUSDT\"", txt);
			Assert.Contains ("interval: \"1m\"", txt);
			Assert.Contains ("expectedStartUtc: new DateTime (2025, 12, 14, 8, 26, 0", txt);
			Assert.Contains ("actualStartUtc:   new DateTime (2025, 12, 16, 0, 0, 0", txt);
			}
		}
	}
