using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;
using SolSignalModel1D_Backtest.Core.Causal.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Causal.Utils;

namespace SolSignalModel1D_Backtest.Tests.Init
	{
	/// <summary>
	/// Тесты на инварианты 1m-рядов:
	/// - EnsureSortedAndStrictUnique1m: допускает один раз отсортировать вход, но НЕ допускает дублей/пересечений.
	/// - MergeSortedStrictUnique1m: сливает два заранее отсортированных strict-unique ряда и запрещает overlap.
	///
	/// ВАЖНО: методы находятся в Program как private static, поэтому вызываем через reflection.
	/// Это осознанно: тестируем ровно ту логику, которая реально используется в рантайме.
	/// </summary>
	public sealed class Program1mSeriesGuardsTests
		{
		[Fact]
		public void EnsureSortedAndStrictUnique1m_DoesNothing_ForSortedUnique ()
			{
			var xs = new List<Candle1m>
				{
				M1 ("2020-02-24T00:00:00Z"),
				M1 ("2020-02-24T00:01:00Z"),
				M1 ("2020-02-24T00:02:00Z"),
				};

			InvokeEnsureSortedAndStrictUnique1m (xs, tag: "weekdays");

			AssertStrictlyIncreasing (xs);
			}

		[Fact]
		public void EnsureSortedAndStrictUnique1m_SortsOnce_WhenUnsorted_AndStillStrictUnique ()
			{
			var xs = new List<Candle1m>
				{
				M1 ("2020-02-24T00:02:00Z"),
				M1 ("2020-02-24T00:00:00Z"),
				M1 ("2020-02-24T00:01:00Z"),
				};

			InvokeEnsureSortedAndStrictUnique1m (xs, tag: "weekdays");

			AssertStrictlyIncreasing (xs);
			Assert.Equal (ParseUtc ("2020-02-24T00:00:00Z"), xs[0].OpenTimeUtc);
			Assert.Equal (ParseUtc ("2020-02-24T00:01:00Z"), xs[1].OpenTimeUtc);
			Assert.Equal (ParseUtc ("2020-02-24T00:02:00Z"), xs[2].OpenTimeUtc);
			}

		[Fact]
		public void EnsureSortedAndStrictUnique1m_Throws_OnDuplicateTimes ()
			{
			var xs = new List<Candle1m>
				{
				M1 ("2020-02-24T00:00:00Z"),
				M1 ("2020-02-24T00:01:00Z"),
				M1 ("2020-02-24T00:01:00Z"), // дубль
				};

			var ex = Assert.Throws<InvalidOperationException> (
				() => InvokeEnsureSortedAndStrictUnique1m (xs, tag: "weekdays"));

			Assert.Contains ("non-strict time sequence", ex.Message);
			Assert.Contains ("Дубли/пересечения", ex.Message);
			}

		[Fact]
		public void MergeSortedStrictUnique1m_MergesWithoutOverlap ()
			{
			var a = new List<Candle1m>
				{
				M1 ("2020-02-24T00:00:00Z"),
				M1 ("2020-02-24T00:02:00Z"),
				};

			var b = new List<Candle1m>
				{
				M1 ("2020-02-24T00:01:00Z"),
				M1 ("2020-02-24T00:03:00Z"),
				};

			var merged = InvokeMergeSortedStrictUnique1m (a, b);

			Assert.Equal (4, merged.Count);
			AssertStrictlyIncreasing (merged);
			Assert.Equal (ParseUtc ("2020-02-24T00:00:00Z"), merged[0].OpenTimeUtc);
			Assert.Equal (ParseUtc ("2020-02-24T00:01:00Z"), merged[1].OpenTimeUtc);
			Assert.Equal (ParseUtc ("2020-02-24T00:02:00Z"), merged[2].OpenTimeUtc);
			Assert.Equal (ParseUtc ("2020-02-24T00:03:00Z"), merged[3].OpenTimeUtc);
			}

		[Fact]
		public void MergeSortedStrictUnique1m_Throws_OnOverlapBetweenWeekdaysAndWeekends ()
			{
			var a = new List<Candle1m>
				{
				M1 ("2020-02-24T00:00:00Z"),
				M1 ("2020-02-24T00:01:00Z"),
				};

			var b = new List<Candle1m>
				{
				M1 ("2020-02-24T00:01:00Z"), // overlap
				M1 ("2020-02-24T00:02:00Z"),
				};

			var ex = Assert.Throws<InvalidOperationException> (() => InvokeMergeSortedStrictUnique1m (a, b));
			Assert.Contains ("overlap between weekdays/weekends", ex.Message);
			}

		[Fact]
		public void MergeSortedStrictUnique1m_Throws_IfInputsViolateSortedInvariant ()
			{
			// Метод ожидает уже отсортированные входы. Если это не так, алгоритм может
			// сгенерировать нестрого возрастающий итог и обязан упасть по своему инварианту.
			var a = new List<Candle1m>
				{
				M1 ("2020-02-24T00:02:00Z"),
				M1 ("2020-02-24T00:00:00Z"), // инверсия
				};

			var b = new List<Candle1m>
				{
				M1 ("2020-02-24T00:01:00Z"),
				};

			var ex = Assert.Throws<InvalidOperationException> (() => InvokeMergeSortedStrictUnique1m (a, b));
			Assert.Contains ("merged: non-strict time sequence", ex.Message);
			}

		// =========================
		// Reflection helpers
		// =========================

		private static void InvokeEnsureSortedAndStrictUnique1m ( List<Candle1m> xs, string tag )
			{
			var mi = typeof (global::SolSignalModel1D_Backtest.Program)
				.GetMethod ("EnsureSortedAndStrictUnique1m", BindingFlags.NonPublic | BindingFlags.Static);

			if (mi == null)
				throw new InvalidOperationException ("EnsureSortedAndStrictUnique1m method not found via reflection.");

			try
				{
				mi.Invoke (null, new object[] { xs, tag });
				}
			catch (TargetInvocationException ex) when (ex.InnerException != null)
				{
				throw ex.InnerException;
				}
			}

		private static List<Candle1m> InvokeMergeSortedStrictUnique1m ( List<Candle1m> a, List<Candle1m> b )
			{
			var mi = typeof (global::SolSignalModel1D_Backtest.Program)
				.GetMethod ("MergeSortedStrictUnique1m", BindingFlags.NonPublic | BindingFlags.Static);

			if (mi == null)
				throw new InvalidOperationException ("MergeSortedStrictUnique1m method not found via reflection.");

			try
				{
				return (List<Candle1m>) mi.Invoke (null, new object[] { a, b })!;
				}
			catch (TargetInvocationException ex) when (ex.InnerException != null)
				{
				throw ex.InnerException;
				}
			}

		// =========================
		// Test utilities
		// =========================

		private static Candle1m M1 ( string isoUtc )
			{
			var t = ParseUtc (isoUtc);

			// OHLC здесь не важен: тестируем только временную ось и инварианты merge/unique.
			return new Candle1m
				{
				OpenTimeUtc = t,
				Open = 1,
				High = 1,
				Low = 1,
				Close = 1,
				};
			}

		private static DateTime ParseUtc ( string isoUtc )
			{
			// DateTimeStyles.RoundtripKind сохраняет Kind=Utc по "Z".
			return DateTime.Parse (isoUtc, null, System.Globalization.DateTimeStyles.RoundtripKind);
			}

		private static void AssertStrictlyIncreasing ( IReadOnlyList<Candle1m> xs )
			{
			for (int i = 1; i < xs.Count; i++)
				{
				Assert.True (
					xs[i].OpenTimeUtc > xs[i - 1].OpenTimeUtc,
					$"Non-strict time sequence at idx={i}: prev={xs[i - 1].OpenTimeUtc:O}, cur={xs[i].OpenTimeUtc:O}");
				}
			}
		}
	}
