using SolSignalModel1D_Backtest.Core.Data;
using Xunit;

namespace SolSignalModel1D_Backtest.Tests.Data.DataBuilder
	{
	/// <summary>
	/// Тесты для диапазонного загрузчика klines:
	/// - нормальные "пустые" диапазоны (to<=from в пределах одного интервала) не падают и не ходят в сеть;
	/// - по-настоящему битые диапазоны (to<<from, гораздо дальше длины интервала) выбрасывают ArgumentException.
	/// </summary>
	public class DataLoadingKlinesRangeTests
		{
		private sealed class ThrowingHandler : HttpMessageHandler
			{
			protected override Task<HttpResponseMessage> SendAsync (
				HttpRequestMessage request,
				CancellationToken cancellationToken )
				{
				// Если этот код вызывается в тестах "no-op", значит, ранний выход не сработал.
				throw new InvalidOperationException ("HTTP-путь не должен вызываться в этом тесте.");
				}
			}

		[Fact]
		public async Task GetBinanceKlinesRange_NoOp_WhenToUtcIsSlightlyBeforeFromUtc_ForKnownInterval ()
			{
			// Arrange.
			var handler = new ThrowingHandler ();
			using var http = new HttpClient (handler);

			const string symbol = "SOLUSDT";
			const string interval = "6h";

			// Имитация кейса "следующая 6h-свеча ещё не началась":
			// fromUtc позже toUtc, но разница меньше длины интервала.
			var toUtc = new DateTime (2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
			var fromUtc = toUtc.AddHours (3); // diff = 3h < 6h

			// Act.
			var result = await DataLoading.GetBinanceKlinesRange (
				http,
				symbol,
				interval,
				fromUtc,
				toUtc);

			// Assert.
			// Ожидаем, что это нормальный "пустой" диапазон:
			// - нет исключения;
			// - список пустой;
			// - HTTP вообще не вызывался (иначе ThrowingHandler кинет InvalidOperationException).
			Assert.NotNull (result);
			Assert.Empty (result);
			}

		[Fact]
		public async Task GetBinanceKlinesRange_Throws_WhenToUtcMuchEarlierThanFromUtc_ForKnownInterval ()
			{
			// Arrange.
			var handler = new ThrowingHandler ();
			using var http = new HttpClient (handler);

			const string symbol = "SOLUSDT";
			const string interval = "6h";

			// Разрыв существенно больше длины интервала: это уже аномалия,
			// которую метод не должен маскировать "тихим" no-op.
			var toUtc = new DateTime (2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
			var fromUtc = toUtc.AddHours (7); // diff = 7h > 6h

			// Act + Assert.
			await Assert.ThrowsAsync<ArgumentException> (async () =>
				await DataLoading.GetBinanceKlinesRange (
					http,
					symbol,
					interval,
					fromUtc,
					toUtc));
			}

		[Fact]
		public async Task GetBinanceKlinesRange_Throws_WhenIntervalUnknown_AndToUtcBeforeFromUtc ()
			{
			// Arrange.
			var handler = new ThrowingHandler ();
			using var http = new HttpClient (handler);

			const string symbol = "SOLUSDT";
			const string interval = "weird_tf"; // TryGetBinanceIntervalLength вернёт null.

			var toUtc = new DateTime (2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
			var fromUtc = toUtc.AddHours (1); // diff > 0, но длина интервала неизвестна.

			// Act + Assert.
			// Для неизвестного интервала нет безопасного "окна терпимости",
			// поэтому любой кейс to<=from считаем ошибочным.
			await Assert.ThrowsAsync<ArgumentException> (async () =>
				await DataLoading.GetBinanceKlinesRange (
					http,
					symbol,
					interval,
					fromUtc,
					toUtc));
			}
		}
	}
