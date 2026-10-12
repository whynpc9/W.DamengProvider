using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TP07.TemporalDesign.Migrations;

[DbContext(typeof(DesignContext))]
public sealed class DesignContextSnapshot:ModelSnapshot
{
    protected override void BuildModel(ModelBuilder model)=>DesignContext.ConfigureModel(model,Program.Current.Spec!,7);
}
