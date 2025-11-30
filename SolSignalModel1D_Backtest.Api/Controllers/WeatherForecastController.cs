using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SolSignalModel1D_Backtest.Api.Controllers
	{
	[Authorize]
	[ApiController]
	[Route ("[controller]")]
	public class WeatherForecastController : ControllerBase
		{
		// —татический набор текстовых описаний дл€ примера
		private static readonly string[] Summaries = new[]
		{
			"Freezing", "Bracing", "Chilly", "Cool", "Mild",
			"Warm", "Balmy", "Hot", "Sweltering", "Scorching"
		};

		private readonly ILogger<WeatherForecastController> _logger;

		public WeatherForecastController ( ILogger<WeatherForecastController> logger )
			{
			_logger = logger;
			}

		[HttpGet (Name = "GetWeatherForecast")]
		public IEnumerable<WeatherForecast> Get ()
			{
			// ѕроста€ генераци€ тестовых данных Ч 5 дней с рандомной температурой
			return Enumerable
				.Range (1, 5)
				.Select (index => new WeatherForecast
					{
					Date = DateOnly.FromDateTime (DateTime.Now.AddDays (index)),
					TemperatureC = Random.Shared.Next (-20, 55),
					Summary = Summaries[Random.Shared.Next (Summaries.Length)]
					})
				.ToArray ();
			}
		}
	}
