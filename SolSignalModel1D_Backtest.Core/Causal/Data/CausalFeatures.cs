using System;

namespace SolSignalModel1D_Backtest.Core.Causal.Data
	{
	/// <summary>
	/// Набор "сырых" фич, которые подготавливаются билдерами данных (индикаторы, ретёрны, контекст).
	///
	/// Семантика null:
	/// - null = фича не рассчитана / отсутствует (не хватает истории, источник недоступен, дырка в ряду).
	/// - нельзя кодировать "нет данных" как 0/false: это уничтожает различимость "нет данных" vs "реальный ноль".
	///
	/// Практика:
	/// - перед подачей в ML-вектор обычно делается явная нормализация/импутация;
	/// - в диагностике/принтерах null нужно обрабатывать явно (иначе будут CS0266 и скрытые NaN).
	/// </summary>
	public sealed class CausalFeatures
		{
		// ===== Volatility =====

		/// <summary>
		/// ATR в долях от цены (например 0.02 = 2%).
		/// Нужен как базовая оценка волатильности для режима/порогов и minMove.
		/// null обычно означает "warm-up" (не хватило окна ATR) или дырку в исходных свечах.
		/// </summary>
		public double? AtrPct { get; init; }

		/// <summary>
		/// Динамическая волатильность (кастомная метрика), обычно положительная и > 0.
		/// Используется как более "чувствительная" альтернатива ATR для адаптивных порогов.
		/// null = недостаточно истории/ошибка расчёта.
		/// </summary>
		public double? DynVol { get; init; }

		// ===== Alt pulse =====

		/// <summary>
		/// Доля положительных альткоинов за 6h-окно (0..1).
		/// Сигнал краткосрочного risk-on/risk-off по альтрынку.
		/// </summary>
		public double? AltFracPos6h { get; init; }

		/// <summary>
		/// Доля положительных альткоинов за 24h-окно (0..1).
		/// Более "инертный" аналог AltFracPos6h.
		/// </summary>
		public double? AltFracPos24h { get; init; }

		/// <summary>
		/// Медианный ретёрн по альткоинам за 24h (обычно в долях: 0.01 = +1%).
		/// Полезно, когда доля позитивных высокая, но движение маленькое (или наоборот).
		/// </summary>
		public double? AltMedian24h { get; init; }

		/// <summary>
		/// Надёжность alt-источника/оценки.
		/// true = данные полные и согласованные; false = данные есть, но качество ниже порога;
		/// null = источник/расчёт отсутствуют.
		/// </summary>
		public bool? AltReliable { get; init; }

		// ===== SOL returns =====

		/// <summary>
		/// Ретёрн SOL за 30 * 6h-шагов (в долях: -0.05 = -5%).
		/// Используется для режима и "контекста" тренда.
		/// </summary>
		public double? SolRet30 { get; init; }

		/// <summary>
		/// Ретёрн SOL за 3 * 6h-шагa (в долях).
		/// Промежуточный горизонт — часто коррелирует с "инерцией".
		/// </summary>
		public double? SolRet3 { get; init; }

		/// <summary>
		/// Ретёрн SOL за 1 * 6h-шаг (в долях).
		/// Самый короткий горизонт, нужен для short-term фильтров/перекосов.
		/// </summary>
		public double? SolRet1 { get; init; }

		// ===== BTC returns / trend =====

		/// <summary>
		/// Ретёрн BTC за 1 * 6h (в долях).
		/// Часто используется как фильтр "можно ли лонговать альт" при слабом BTC.
		/// </summary>
		public double? BtcRet1 { get; init; }

		/// <summary>
		/// Ретёрн BTC за 30 * 6h (в долях).
		/// Длинный горизонт для режима (risk-off, downtrend).
		/// </summary>
		public double? BtcRet30 { get; init; }

		/// <summary>
		/// Относительное положение BTC относительно SMA200 (в долях: (Close-SMA)/SMA).
		/// Нужен как прокси тренда/режима без "заглядывания" в будущее.
		/// </summary>
		public double? BtcVs200 { get; init; }

		// ===== EMA relations =====

		/// <summary>
		/// Отношение EMA50 vs EMA200 по SOL: (EMA50-EMA200)/EMA200.
		/// Удобный "мягкий" индикатор тренда без пороговой логики.
		/// </summary>
		public double? SolEma50vs200 { get; init; }

		/// <summary>
		/// Отношение EMA50 vs EMA200 по BTC: (EMA50-EMA200)/EMA200.
		/// Часто используется как фильтр для направления (особенно для up-дней).
		/// </summary>
		public double? BtcEma50vs200 { get; init; }

		// ===== Market context =====

		/// <summary>
		/// Fear & Greed Index (0..100).
		/// null = источник недоступен/нет точки на дату.
		/// </summary>
		public double? Fng { get; init; }

		/// <summary>
		/// Изменение DXY за 30 шагов/дней (в долях, например 0.01 = +1%).
		/// Обычно inverse-контекст к risk assets, поэтому важно для фильтров режима.
		/// </summary>
		public double? DxyChg30 { get; init; }

		/// <summary>
		/// Изменение золота за 30 шагов/дней (в долях).
		/// Контекст risk-off/risk-on и корреляционная поправка к крипте.
		/// </summary>
		public double? GoldChg30 { get; init; }

		/// <summary>
		/// RSI SOL, центрированный вокруг 0: (RSI - 50).
		/// null на старте истории или при недостатке окна RSI.
		/// </summary>
		public double? SolRsiCentered { get; init; }

		/// <summary>
		/// Наклон RSI (дельта/тренд) за 3 шага.
		/// Используется как прокси ускорения/замедления momentum.
		/// </summary>
		public double? RsiSlope3 { get; init; }

		// ===== Session/time flags =====

		/// <summary>
		/// Флаг "NY morning" (или другой выбранный критерий утренней сессии).
		/// true/false = определено; null = не смогли определить (например, TZ/окна не заданы).
		/// </summary>
		public bool? IsMorning { get; init; }

		// ===== MetaDecider inputs (liquidity/fibo overlays) =====

		/// <summary>
		/// Относительная "ликвидность/кластер" сверху (нормализованная метрика).
		/// Интерпретация зависит от источника: важнее сравнение по времени, чем абсолютное значение.
		/// </summary>
		public double? LiqUpRel { get; init; }

		/// <summary>
		/// Относительная "ликвидность/кластер" снизу (нормализованная метрика).
		/// </summary>
		public double? LiqDownRel { get; init; }

		/// <summary>
		/// Относительное положение/вес уровня Fibo сверху (нормализовано).
		/// Используется как контекст сопротивления/потенциала хода.
		/// </summary>
		public double? FiboUpRel { get; init; }

		/// <summary>
		/// Относительное положение/вес уровня Fibo снизу (нормализовано).
		/// Используется как контекст поддержки/потенциала отката.
		/// </summary>
		public double? FiboDownRel { get; init; }
		}
	}
