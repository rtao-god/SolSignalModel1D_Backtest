using SolSignalModel1D_Backtest.Core.Causal.Data;

namespace SolSignalModel1D_Backtest.Core.Features
	{
	// 1. Интерфейс: каждую фичу мы пишем как класс, который умеет Build(...)
	public interface IFeatureBuilder
		{
		void Build ( FeatureContext ctx );
		}

	// 2. Контекст: оборачивает одну DataRow и даёт список, куда фичи складывать
	public sealed class FeatureContext
		{
		public DataRow Row { get; }
		public List<double> Features { get; }

		public FeatureContext ( DataRow row )
			{
			Row = row ?? throw new ArgumentNullException (nameof (row));
			Features = new List<double> (64);
			}

		public void Add ( double v )
			{
			// чтобы не ловить NaN/Inf в модели
			Features.Add (double.IsFinite (v) ? v : 0.0);
			}
		}

	// 3. Пайплайн: просто прогоняет контекст по всем билдерам
	public sealed class FeaturePipeline
		{
		private readonly IFeatureBuilder[] _builders;

		public FeaturePipeline ( params IFeatureBuilder[] builders )
			{
			_builders = builders;
			}

		public void Run ( FeatureContext ctx )
			{
			foreach (var b in _builders)
				b.Build (ctx);
			}
		}
	}
