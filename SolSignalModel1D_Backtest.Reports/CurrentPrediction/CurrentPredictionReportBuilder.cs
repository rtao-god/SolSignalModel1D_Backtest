using SolSignalModel1D_Backtest.Core.Analytics.CurrentPrediction;
using SolSignalModel1D_Backtest.Core.Domain;
using SolSignalModel1D_Backtest.Reports.Model;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SolSignalModel1D_Backtest.Reports.CurrentPrediction
	{
	/// <summary>
	/// Строит ReportDocument для "текущего прогноза" на основе CurrentPredictionSnapshot.
	/// Здесь только форматирование в key-value и таблицы, без новой математики.
	/// </summary>
	public static class CurrentPredictionReportBuilder
		{
		public static ReportDocument? Build ( CurrentPredictionSnapshot snapshot )
			{
			if (snapshot == null)
				return null;

			var doc = new ReportDocument
				{
				Id = $"current-prediction-{snapshot.PredictionDateUtc:yyyyMMdd}",
				Kind = "current_prediction",
				Title = $"Текущий прогноз ({TradingSymbols.SolUsdtDisplay})",
				GeneratedAtUtc = snapshot.GeneratedAtUtc
				};

			// === Общие параметры прогноза (то, что видно юзеру в первую очередь) ===
			var info = new KeyValueSection
				{
				Title = "Общие параметры прогноза"
				};

			// Дата прогноза, без ISO-мусора
			info.Items.Add (new KeyValueItem
				{
				Key = "Дата прогноза (UTC)",
				Value = FormatDateUtc (snapshot.PredictionDateUtc)
				});

			// Основное решение дневной модели (Daily)
			info.Items.Add (new KeyValueItem
				{
				Key = "Основная модель (Daily)",
				Value = BuildMainDirectionLabel (snapshot)
				});

			// Микро-модель (1m), если есть человекочитаемое описание
			if (!string.IsNullOrWhiteSpace (snapshot.MicroDisplay))
				{
				info.Items.Add (new KeyValueItem
					{
					Key = "Микро-модель (1m)",
					Value = snapshot.MicroDisplay
					});
				}

			// Режим рынка: нормальный / в фазе снижения
			info.Items.Add (new KeyValueItem
				{
				Key = "Режим рынка",
				Value = snapshot.RegimeDown
					? "Рынок в фазе снижения"
					: "Рынок в нормальном режиме"
				});

			// Вероятность срабатывания стоп-лосса (из SL-модели)
			info.Items.Add (new KeyValueItem
				{
				Key = "Вероятность срабатывания стоп-лосса",
				Value = $"{snapshot.SlProb:0.0} %"
				});

			// Сигнал SL-модели: аккуратно форматируем, не предполагая точной семантики
			info.Items.Add (new KeyValueItem
				{
				Key = "Сигнал SL-модели",
				Value = FormatSlDecision (snapshot.SlHighDecision)
				});

			// Текущая цена инструмента
			info.Items.Add (new KeyValueItem
				{
				Key = $"Текущая цена {TradingSymbols.SolUsdtDisplay}",
				Value = snapshot.Entry.ToString ("0.0000")
				});

			// Минимальный осмысленный ход (в долях) + сразу человекочитаемый %
			info.Items.Add (new KeyValueItem
				{
				Key = "Минимальный осмысленный ход цены",
				Value = $"{snapshot.MinMove:0.0000} ({snapshot.MinMove * 100:0.0} %)"
				});

			// Комментарий модели, если есть
			if (!string.IsNullOrWhiteSpace (snapshot.Reason))
				{
				info.Items.Add (new KeyValueItem
					{
					Key = "Комментарий модели",
					Value = snapshot.Reason
					});
				}

			doc.KeyValueSections.Add (info);

			// === Forward 24h (baseline на истории) ===
			if (snapshot.Forward24h != null)
				{
				var fwd = snapshot.Forward24h;

				var fwdSection = new KeyValueSection
					{
					Title = "Диапазон цены за 24 часа (исторический baseline)"
					};

				fwdSection.Items.Add (new KeyValueItem
					{
					Key = "Максимальная цена за 24 часа",
					Value = fwd.MaxHigh.ToString ("0.0000")
					});
				fwdSection.Items.Add (new KeyValueItem
					{
					Key = "Минимальная цена за 24 часа",
					Value = fwd.MinLow.ToString ("0.0000")
					});
				fwdSection.Items.Add (new KeyValueItem
					{
					Key = "Цена закрытия через 24 часа",
					Value = fwd.Close.ToString ("0.0000")
					});

				doc.KeyValueSections.Add (fwdSection);
				}

			// === Таблица по политикам (BASE vs ANTI-D) ===
			var table = new TableSection
				{
				Title = "Политики плеча (BASE vs ANTI-D)"
				};

			// Более понятные колонки для человека
			table.Columns.AddRange (new[]
			{
				"Политика",
				"Ветка",
				"Рискованный день",
				"Есть направление",
				"Пропущено",
				"Направление",
				"Плечо",
				"Цена входа",
				"SL, %",
				"TP, %",
				"Цена SL",
				"Цена TP",
				"Размер позиции, $",
				"Размер позиции, qty",
				"Цена ликвидации",
				"Дистанция до ликвидации, %"
			});

			foreach (var row in snapshot.PolicyRows)
				{
				string F ( double? v, string fmt ) => v.HasValue ? v.Value.ToString (fmt) : "-";

				table.Rows.Add (new List<string>
				{
					row.PolicyName,
					row.Branch,
					row.IsRiskDay.ToString(),
					row.HasDirection.ToString(),
					row.Skipped.ToString(),
					row.Direction,
					row.Leverage.ToString("0.##"),
					row.Entry.ToString("0.0000"),
					row.SlPct.HasValue ? row.SlPct.Value.ToString("0.0") : "-",
					row.TpPct.HasValue ? row.TpPct.Value.ToString("0.0") : "-",
					F(row.SlPrice, "0.0000"),
					F(row.TpPrice, "0.0000"),
					F(row.PositionUsd, "0.00"),
					F(row.PositionQty, "0.000"),
					F(row.LiqPrice, "0.0000"),
					F(row.LiqDistPct, "0.0")
				});
				}

			doc.TableSections.Add (table);

			return doc;
			}

		/// <summary>
		/// Форматирует дату прогноза в человекочитаемый UTC-вид без миллисекунд.
		/// Пример: 2025-11-26 12:00 UTC.
		/// </summary>
		private static string FormatDateUtc ( DateTime dtUtc )
			{
			var utc = dtUtc.Kind == DateTimeKind.Utc ? dtUtc : dtUtc.ToUniversalTime ();
			return utc.ToString ("yyyy-MM-dd HH:mm 'UTC'");
			}

		/// <summary>
		/// Строит подпись основной дневной модели:
		/// "Рост", "Падение", "Боковик", "Боковик-Рост", "Боковик-Падение".
		///
		/// Важно: здесь используется разумная гипотеза по PredLabel:
		/// -1 ~ падение, 0 ~ боковик, +1 ~ рост.
		/// Если в твоей реализации другая кодировка — маппинг нужно
		/// подправить под фактические значения.
		/// </summary>
		private static string BuildMainDirectionLabel ( CurrentPredictionSnapshot snapshot )
			{
			// PredLabel в текущей модели — целое (обычно -1/0/+1).
			// Здесь предполагается:
			//  -1 → падение
			//   0 → боковик
			//  +1 → рост
			// Если в реальной схеме кодировка другая — маппинг ниже нужно подправить.
			var rawLabel = snapshot.PredLabel;
			var raw = rawLabel.ToString (CultureInfo.InvariantCulture);
			var rawTrim = raw.Trim ();
			var rawLower = rawTrim.ToLowerInvariant ();

			bool? baseFlat = null;
			bool? baseUp = null;

			// Базовая интерпретация по числовому коду
			if (rawLabel == 0)
				{
				baseFlat = true;
				}
			else if (rawLabel > 0)
				{
				baseFlat = false;
				baseUp = true;
				}
			else if (rawLabel < 0)
				{
				baseFlat = false;
				baseUp = false;
				}

			// Доп. обработка текстовых вариантов, если когда-нибудь PredLabel станет строкой
			if (rawLower.IndexOf ("flat", StringComparison.Ordinal) >= 0
				|| rawLower.IndexOf ("флэт", StringComparison.Ordinal) >= 0
				|| rawLower.IndexOf ("sideways", StringComparison.Ordinal) >= 0)
				{
				baseFlat ??= true;
				}

			if (rawLower.IndexOf ("up", StringComparison.Ordinal) >= 0
				|| rawLower.IndexOf ("long", StringComparison.Ordinal) >= 0
				|| rawLower.IndexOf ("рост", StringComparison.Ordinal) >= 0)
				{
				baseUp ??= true;
				}

			if (rawLower.IndexOf ("down", StringComparison.Ordinal) >= 0
				|| rawLower.IndexOf ("short", StringComparison.Ordinal) >= 0
				|| rawLower.IndexOf ("пад", StringComparison.Ordinal) >= 0)
				{
				baseUp ??= false;
				}

			// Микро-модель: используем для уточнения боковика
			var micro = snapshot.MicroDisplay?.ToLowerInvariant () ?? string.Empty;
			bool microUp = micro.Contains ("up") || micro.Contains ("рост");
			bool microDown = micro.Contains ("down") || micro.Contains ("пад");

			if (baseFlat == true)
				{
				if (microUp)
					return "Боковик-Рост";
				if (microDown)
					return "Боковик-Падение";
				return "Боковик";
				}

			if (baseUp == true)
				return "Рост";

			if (baseUp == false)
				return "Падение";

			// Фолбэк: показываем сырой код, если ничего не распознали
			return string.IsNullOrWhiteSpace (rawTrim) ? "нет данных" : rawTrim;
			}

		/// <summary>
		/// Человекочитаемое описание решения SL-модели.
		/// Тип SlHighDecision заранее неизвестен, поэтому работаем через ToString.
		/// Для bool / 0/1 возвращаем внятный текст, для остальных значений — сырой вывод.
		/// </summary>
		private static string FormatSlDecision ( object? slDecision )
			{
			if (slDecision == null)
				return "нет данных";

			var raw = slDecision.ToString () ?? string.Empty;
			var lower = raw.Trim ().ToLowerInvariant ();

			if (lower == "true" || lower == "1" || lower == "high")
				return "Высокий риск: стопы стоит усилить";

			if (lower == "false" || lower == "0" || lower == "ok")
				return "Нормальный риск по стопам";

			// Если это какой-то специфический enum/строка — показываем как есть.
			return raw;
			}
		}
	}
