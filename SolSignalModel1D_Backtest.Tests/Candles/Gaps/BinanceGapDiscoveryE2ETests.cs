using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using SolSignalModel1D_Backtest.Core.Data.Candles.Gaps;
using SolSignalModel1D_Backtest.Core.Infra;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Data.Candles.Gaps
	{
	/// <summary>
	/// E2E-тест для "одним запуском найти все дыры Binance".
	///
	/// Почему тест, а не Program:
	/// - тебе не нужно понимать, куда "вставлять вызов";
	/// - запускается одной командой dotnet test;
	/// - пишет отчёт в файл, чтобы можно было просто скопировать KnownCandleGap блок в CandleDataGaps.
	///
	/// Важно:
	/// - тест делает HTTP-запросы к Binance, может идти несколько минут;
	/// - если у тебя VPN/лимиты/нестабильный интернет — gaps могут стать flaky.
	/// </summary>
	public sealed class BinanceGapDiscoveryE2ETests
		{
		[Fact]
		public async Task Scan_SOLUSDT_1m_AllGaps_StableVsFlaky_ReportToFile ()
			{
			var fromUtc = new DateTime (2021, 8, 2, 0, 0, 0, DateTimeKind.Utc);

			// toUtcExclusive выравниваем до минуты (иначе BinanceGapDiscovery специально упадёт).
			var toUtcExclusive = RoundDownToMinuteUtc (DateTime.UtcNow);

			var opt = new BinanceGapDiscovery.Options
				{
				Passes = 3,
				ThrottleDelay = TimeSpan.FromMilliseconds (150),
				MaxPageRetries = 4,
				RetryBaseDelay = TimeSpan.FromMilliseconds (250),
				IncludeFlakyInAggregates = true
				};

			using var http = new HttpClient
				{
				Timeout = TimeSpan.FromSeconds (25)
				};

			var rep = await BinanceGapDiscovery.RunAsync (
				http: http,
				symbol: "SOLUSDT",
				interval: "1m",
				fromUtc: fromUtc,
				toUtcExclusive: toUtcExclusive,
				opt: opt);

			var stable = rep.Aggregates.Where (a => a.IsStable).ToList ();
			var flaky = rep.Aggregates.Where (a => !a.IsStable).ToList ();

			var sb = new StringBuilder ();
			sb.AppendLine ($"Binance gap scan report");
			sb.AppendLine ($"symbol={rep.Symbol}, interval={rep.Interval}");
			sb.AppendLine ($"range=[{rep.FromUtc:O}..{rep.ToUtcExclusive:O})");
			sb.AppendLine ($"passes={rep.Passes.Count}");
			sb.AppendLine ();

			foreach (var p in rep.Passes)
				sb.AppendLine ($"pass#{p.PassNo}: pages={p.Pages}, bars={p.Bars}, gaps={p.Gaps.Count}");

			sb.AppendLine ();
			sb.AppendLine ("=== STABLE gaps (seen in ALL passes) ===");
			foreach (var a in stable)
				sb.AppendLine ($"{a.Gap} (seen={a.SeenInPasses}/{a.TotalPasses})");

			sb.AppendLine ();
			sb.AppendLine ("=== FLAKY gaps (NOT seen in all passes) ===");
			foreach (var a in flaky)
				sb.AppendLine ($"{a.Gap} (seen={a.SeenInPasses}/{a.TotalPasses})");

			sb.AppendLine ();
			sb.AppendLine ("=== C# snippet: add these to CandleDataGaps.Known1mGaps (ONLY STABLE) ===");
			sb.AppendLine (BinanceGapDiscovery.ToKnownCandleGapCSharp (rep.Aggregates, onlyStable: true));

			// Пишем рядом с candles, чтобы файл было легко найти.
			var outDir = Path.Combine (PathConfig.CandlesDir, "_gaps");
			Directory.CreateDirectory (outDir);

			var outPath = Path.Combine (outDir, $"binance-gap-scan-SOLUSDT-1m-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
			File.WriteAllText (outPath, sb.ToString (), Encoding.UTF8);

			// Печать в консоль теста — чтобы ты сразу видел ключевое.
			Console.WriteLine (sb.ToString ());
			Console.WriteLine ($"[binance-gap-scan] report saved: {outPath}");

			// Тест не должен "падать" из-за наличия дыр — это диагностический инструмент.
			Assert.True (rep.Passes.Count == opt.Passes);
			}

		private static DateTime RoundDownToMinuteUtc ( DateTime utc )
			{
			if (utc.Kind != DateTimeKind.Utc)
				utc = utc.ToUniversalTime ();

			return new DateTime (utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
			}
		}
	}
