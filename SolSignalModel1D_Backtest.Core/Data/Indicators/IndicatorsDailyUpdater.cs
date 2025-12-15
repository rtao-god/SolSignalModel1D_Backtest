using SolSignalModel1D_Backtest.Core.Infra;
using SolSignalModel1D_Backtest.Core.Utils.Time;

namespace SolSignalModel1D_Backtest.Core.Data.Indicators
	{
	/// <summary>
	/// Обновляет NDJSON-кэш индикаторов (FNG, DXY) "дописыванием".
	/// </summary>
	public sealed class IndicatorsDailyUpdater
		{
		public enum FillMode { Strict = 0, NeutralFill = 1 }

		private readonly HttpClient _http;
		private readonly IndicatorsNdjsonStore _fngStore;
		private readonly IndicatorsNdjsonStore _dxyStore;

		public IndicatorsDailyUpdater ( HttpClient http )
			{
			_http = http;
			var fngPath = Path.Combine (PathConfig.IndicatorsDir, "fng.ndjson");
			var dxyPath = Path.Combine (PathConfig.IndicatorsDir, "dxy.ndjson");
			_fngStore = new IndicatorsNdjsonStore (fngPath);
			_dxyStore = new IndicatorsNdjsonStore (dxyPath);
			}

		public async Task UpdateAllAsync ( DateTime rangeStartUtc, DateTime rangeEndUtc, FillMode fillMode )
			{
			await UpdateFngAsync (rangeStartUtc, rangeEndUtc, fillMode);
			await UpdateDxyAsync (rangeStartUtc, rangeEndUtc, fillMode);
			}

		public void EnsureCoverageOrFail ( DateTime rangeStartUtc, DateTime rangeEndUtc )
			{
			var fng = _fngStore.ReadRange (rangeStartUtc, rangeEndUtc);
			var dxy = _dxyStore.ReadRange (rangeStartUtc, rangeEndUtc);
			var missing = new List<string> ();

			for (var d = rangeStartUtc.ToCausalDateUtc(); d <= rangeEndUtc.ToCausalDateUtc(); d = d.AddDays (1))
				{
				if (!fng.ContainsKey (d)) missing.Add ($"FNG@{d:yyyy-MM-dd}");
				if (!dxy.ContainsKey (d)) missing.Add ($"DXY@{d:yyyy-MM-dd}");
				}
			if (missing.Count > 0)
				throw new InvalidOperationException ("[indicators] missing: " + string.Join (", ", missing));
			}

		public Dictionary<DateTime, double> LoadFngDict ( DateTime startUtc, DateTime endUtc )
			{
			var raw = _fngStore.ReadRange (startUtc, endUtc);
			var res = new Dictionary<DateTime, double> ();
			foreach (var kv in raw)
				res[kv.Key] = (int) Math.Round (kv.Value);
			return res;
			}

		public Dictionary<DateTime, double> LoadDxyDict ( DateTime startUtc, DateTime endUtc )
			=> _dxyStore.ReadRange (startUtc, endUtc);

		private async Task UpdateFngAsync ( DateTime startUtc, DateTime endUtc, FillMode fillMode )
			{
			DateTime from = _fngStore.TryGetLastDate ()?.AddDays (1) ?? startUtc.ToCausalDateUtc();
			if (from > endUtc.ToCausalDateUtc()) return;

			var fresh = await DataLoading.GetFngHistory (_http); // Dict<Date,int>

			var lines = new List<IndicatorsNdjsonStore.IndicatorLine> ();
			var missingHard = new List<DateTime> ();

			for (var d = from.ToCausalDateUtc(); d <= endUtc.ToCausalDateUtc(); d = d.AddDays (1))
				{
				if (fresh.TryGetValue (d, out var fng))
					{
					lines.Add (new IndicatorsNdjsonStore.IndicatorLine (d, fng));
					}
				else
					{
					if (fillMode == FillMode.Strict) missingHard.Add (d);
					else lines.Add (new IndicatorsNdjsonStore.IndicatorLine (d, 50.0));
					}
				}

			if (missingHard.Count > 0)
				throw new InvalidOperationException ("[indicators:fng] missing days: " +
					string.Join (", ", missingHard.Select (d => d.ToString ("yyyy-MM-dd"))));

			if (lines.Count > 0)
				{
				_fngStore.Append (lines);
				Console.WriteLine ($"[indicators] FNG appended {lines.Count} days ({from:yyyy-MM-dd}..{endUtc:yyyy-MM-dd})");
				}
			}

		private async Task UpdateDxyAsync ( DateTime startUtc, DateTime endUtc, FillMode fillMode )
			{
			DateTime from = _dxyStore.TryGetLastDate ()?.AddDays (1) ?? startUtc.ToCausalDateUtc();
			if (from > endUtc.ToCausalDateUtc()) return;

			var fetched = await DataLoading.GetDxySeries (_http, from.AddDays (-10), endUtc);
			var lines = new List<IndicatorsNdjsonStore.IndicatorLine> ();
			var missingHard = new List<DateTime> ();

			double? lastKnown = null;
			var earliestHave = fetched.OrderBy (kv => kv.Key).FirstOrDefault ();
			if (!double.IsNaN (earliestHave.Value)) lastKnown = earliestHave.Value;

			for (var d = from.ToCausalDateUtc(); d <= endUtc.ToCausalDateUtc(); d = d.AddDays (1))
				{
				if (fetched.TryGetValue (d, out double v))
					{
					lastKnown = v;
					lines.Add (new IndicatorsNdjsonStore.IndicatorLine (d, v));
					}
				else
					{
					if (lastKnown.HasValue && fillMode == FillMode.NeutralFill)
						lines.Add (new IndicatorsNdjsonStore.IndicatorLine (d, lastKnown.Value));
					else
						missingHard.Add (d);
					}
				}

			if (missingHard.Count > 0)
				throw new InvalidOperationException ("[indicators:dxy] missing days: " +
					string.Join (", ", missingHard.Select (d => d.ToString ("yyyy-MM-dd"))));

			if (lines.Count > 0)
				{
				_dxyStore.Append (lines);
				Console.WriteLine ($"[indicators] DXY appended {lines.Count} days ({from:yyyy-MM-dd}..{endUtc:yyyy-MM-dd})");
				}
			}
		}
	}
