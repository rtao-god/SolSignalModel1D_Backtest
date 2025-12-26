using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SolSignalModel1D_Backtest.Core.Data.Indicators
	{
	/// <summary>
	/// Диагностика временных рядов (Dictionary<DateTime,double>) для сообщений об ошибках.
	/// Используется строго на error-path: аллокации/сортировки допустимы.
	/// </summary>
	internal static class IndicatorSeriesDiagnostics
		{
		public static string DescribeMissingKey (
			Dictionary<DateTime, double> series,
			string seriesKey,
			DateTime requiredUtc,
			int neighbors = 6 )
			{
			if (series == null) return $"{seriesKey}=null";
			if (series.Count == 0) return $"{seriesKey}=empty";

			if (requiredUtc.Kind != DateTimeKind.Utc)
				return $"{seriesKey}=invalid requiredUtc kind={requiredUtc.Kind}, t={requiredUtc:O}";

			var keys = series.Keys.OrderBy (x => x).ToArray ();
			var min = keys[0];
			var max = keys[^1];

			int pos = Array.BinarySearch (keys, requiredUtc);
			if (pos < 0) pos = ~pos;

			DateTime? prev = pos > 0 ? keys[pos - 1] : null;
			DateTime? next = pos < keys.Length ? keys[pos] : null;

			int from = Math.Max (0, pos - neighbors);
			int to = Math.Min (keys.Length, pos + neighbors);

			var sb = new StringBuilder ();
			sb.Append (seriesKey);
			sb.Append (": count=").Append (keys.Length);
			sb.Append (", range=[").Append (min.ToString ("O")).Append ("..").Append (max.ToString ("O")).Append ("]");
			sb.Append (", required=").Append (requiredUtc.ToString ("O"));

			sb.Append (", prev=");
			sb.Append (prev.HasValue ? prev.Value.ToString ("O") : "null");

			sb.Append (", next=");
			sb.Append (next.HasValue ? next.Value.ToString ("O") : "null");

			if (requiredUtc < min)
				{
				sb.Append (", requiredBeforeRange=true");
				sb.Append (", requiredMinusMinHours=").Append ((min - requiredUtc).TotalHours.ToString ("0.###"));
				}
			else if (requiredUtc > max)
				{
				sb.Append (", requiredAfterRange=true");
				sb.Append (", requiredMinusMaxHours=").Append ((requiredUtc - max).TotalHours.ToString ("0.###"));
				}

			sb.Append (", around=[");
			for (int i = from; i < to; i++)
				{
				if (i > from) sb.Append (", ");
				sb.Append (keys[i].ToString ("O"));
				}
			sb.Append ("]");

			return sb.ToString ();
			}
		}
	}
