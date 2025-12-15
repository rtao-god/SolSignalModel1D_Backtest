namespace SolSignalModel1D_Backtest.Core.Causal.Features
	{
	/// <summary>
	/// Фичи по SOL. Ничего не "лечим": если данных нет — это ошибка пайплайна/окна истории,
	/// а не повод незаметно подставить 0.
	/// </summary>
	public sealed class SolFeatures : IFeatureBuilder
		{
		public void Build ( FeatureContext ctx )
			{
			var r = ctx.Row;

			// Эти ретёрны обычно отсутствуют на старте истории (нет 30 окон).
			// Здесь выбран fail-fast: если строка дошла до фичебилдера — она должна быть валидной.
			ctx.Add (r.Causal.GetFeatureOrThrow (r.Causal.SolRet30, nameof (r.Causal.SolRet30)));
			ctx.Add (r.Causal.GetFeatureOrThrow (r.Causal.SolRet3, nameof (r.Causal.SolRet3)));
			ctx.Add (r.Causal.GetFeatureOrThrow (r.Causal.SolRet1, nameof (r.Causal.SolRet1)));

			// Временная фича может быть null, если не посчитали/не применили timezone.
			bool isMorning = r.Causal.GetFeatureOrThrow (r.Causal.IsMorning, nameof (r.Causal.IsMorning));
			ctx.Add (isMorning ? 1.0 : 0.0);
			}
		}
	}
