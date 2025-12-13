using System;
using SolSignalModel1D_Backtest.Core.Domain;

namespace SolSignalModel1D_Backtest.Core.Data.DataBuilder
	{
	/// <summary>
	/// Омнисциентная строка для аналитики: causal + forward.
	/// Важно: тренировка/инференс должны принимать только CausalDataRow
	/// (или прямо FeaturesVector из него), а не OmniscientDataRow.
	/// </summary>
	public sealed class OmniscientDataRow : IHasDateUtc
		{
		public CausalDataRow Causal { get; }
		public ForwardOutcomesRow Outcomes { get; }

		public DateTime DateUtc => Causal.DateUtc;

		public OmniscientDataRow ( CausalDataRow causal, ForwardOutcomesRow outcomes )
			{
			Causal = causal ?? throw new ArgumentNullException (nameof (causal));
			Outcomes = outcomes ?? throw new ArgumentNullException (nameof (outcomes));
			}
		}
	}
