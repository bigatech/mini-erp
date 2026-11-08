namespace Cutting.Application;

public static class CuttingRules
{
    public static readonly string[] Statuses = ["Draft", "Planned", "InProgress", "Completed", "Cancelled"];
    public static void Validate(SaveCuttingDocument request, IReadOnlyList<CuttingOrderLine> orderLines)
    {
        var errors = new Dictionary<string,string[]>();
        void Error(string path,string message) => errors.TryAdd(path,[message]);
        void Number(decimal? value,string path,bool integer=false,bool positive=false)
        {
            if(value is {} n && (n<0 || n>1_000_000_000m || (positive&&n==0) || (integer&&n!=decimal.Truncate(n))))
                Error(path, positive?"Pozitif, geçerli bir miktar girin.":"Negatif olmayan, geçerli bir miktar girin.");
        }
        void Dates(DateTimeOffset? start,DateTimeOffset? end,string path)
        { if(start.HasValue!=end.HasValue || (end<start))Error(path,"Başlangıç ve bitiş birlikte girilmeli; bitiş başlangıçtan önce olamaz."); }
        if(request.OrderId==Guid.Empty||request.OrderVersion<=0) Error("orderId","Sipariş ve geçerli sipariş sürümü seçin.");
        if(!Statuses.Contains(request.Status))Error("status","Geçersiz kesim durumu.");
        if(request.Groups is null || request.Groups.Count>100)Error("groups","En fazla 100 malzeme grubu kaydedilebilir.");
        var selections=orderLines.Select(x=>(x.VariantOptionId,x.SizeOptionId,x.SetPartOptionId)).ToHashSet();
        var ids=new HashSet<Guid>();
        void Id(Guid id,string path) { if(id==Guid.Empty||!ids.Add(id)) Error(path,"Kayıt kimliği boş veya tekrar ediyor."); }
        var totalPlans=0;var totalActuals=0;
        foreach(var (group,gi) in (request.Groups??[]).Select((x,i)=>(x,i)))
        {
            var gp=$"groups[{gi}]";
            if(group is null){Error(gp,"Grup boş olamaz.");continue;}
            Id(group.Id,gp+".id");
            if(group.MaterialId==Guid.Empty)Error(gp+".materialId","Malzeme seçin.");
            if(string.IsNullOrWhiteSpace(group.Unit)||group.Unit.Length>32)Error(gp+".unit","Malzemenin geçerli ölçü birimi gereklidir.");
            if(group.WorkOrderNumber?.Length>100||group.ApprovalNumber?.Length>100)Error(gp,"İş emri/onay no en fazla 100 karakterdir.");
            Number(group.FabricKg,gp+".fabricKg");Number(group.LiningM,gp+".liningM");
            if(group.Plans is null){Error(gp+".plans","Plan listesi gereklidir.");continue;}
            var planNumbers=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var (plan,pi) in group.Plans.Select((x,i)=>(x,i)))
            {
                var pp=$"{gp}.plans[{pi}]";totalPlans++;
                if(plan is null){Error(pp,"Plan boş olamaz.");continue;}
                Id(plan.Id,pp+".id");
                if(string.IsNullOrWhiteSpace(plan.Number)||plan.Number.Length>100||!planNumbers.Add(plan.Number.Trim()))Error(pp+".number","Grup içinde tekil pastal no girin (en fazla 100 karakter).");
                Number(plan.Layers,pp+".layers",true,true);Number(plan.LengthM,pp+".lengthM",false,true);Number(plan.WidthM,pp+".widthM",false,true);
                Number(plan.PlannedConsumption,pp+".plannedConsumption");Number(plan.PlannedMinutes,pp+".plannedMinutes");
                Number(plan.PlannedWeightKg,pp+".plannedWeightKg");
                Number(plan.PatternAreaM2,pp+".patternAreaM2");Number(plan.UsableAreaM2,pp+".usableAreaM2",false,true);
                if(plan.PatternAreaM2>plan.UsableAreaM2)Error(pp+".patternAreaM2","Kalıp alanı kullanılabilir tek kat alanını aşamaz.");
                Dates(plan.PlannedStart,plan.PlannedEnd,pp+".plannedStart");
                if(plan.PlannedStart.HasValue&&plan.PlannedEnd.HasValue&&plan.PlannedMinutes is {} mins && mins!=(decimal)(plan.PlannedEnd.Value-plan.PlannedStart.Value).TotalMinutes)
                    Error(pp+".plannedMinutes","Plan süresi ile başlangıç/bitiş aralığı çelişiyor; tek bir süre kaynağı kullanın.");
                if(plan.Lines is not {Count:>0} || plan.Lines.Count>10000){Error(pp+".lines","1–10000 sipariş dağılım satırı gereklidir.");continue;}
                var planKeys=new HashSet<(Guid,Guid,Guid?)>();
                foreach(var (line,li) in plan.Lines.Select((x,i)=>(x,i)))
                {
                    var lp=$"{pp}.lines[{li}]";
                    if(line is null){Error(lp,"Satır boş olamaz.");continue;}
                    var key=(line.VariantOptionId,line.SizeOptionId,line.SetPartOptionId);
                    if(!selections.Contains(key)||!planKeys.Add(key))Error(lp,"Siparişte bulunan, tekrarsız beden/varyant/parça seçin.");
                    Number(line.PiecesPerLayer,lp+".piecesPerLayer",true,true);
                }
                if(plan.Actuals is null){Error(pp+".actuals","Gerçekleşme listesi gereklidir.");continue;}
                foreach(var (actual,ai) in plan.Actuals.Select((x,i)=>(x,i)))
                {
                    var ap=$"{pp}.actuals[{ai}]";totalActuals++;
                    if(actual is null){Error(ap,"Gerçekleşme boş olamaz.");continue;}
                    Id(actual.Id,ap+".id");
                    if(actual.Date==default)Error(ap+".date","İşlem tarihi gereklidir.");
                    Number(actual.Layers,ap+".layers",true,true);Number(actual.GrossConsumption,ap+".grossConsumption");
                    Number(actual.ReturnedConsumption,ap+".returnedConsumption");Number(actual.ReusableConsumption,ap+".reusableConsumption");
                    Number(actual.NetWeightKg,ap+".netWeightKg");
                    if(actual.GrossConsumption is {} gross && (actual.ReturnedConsumption??0)+(actual.ReusableConsumption??0)>gross)Error(ap+".grossConsumption","İade ve kullanılabilir artık brüt tüketimi aşamaz; ayrı miktarlar girin.");
                    Dates(actual.StartedAt,actual.EndedAt,ap+".startedAt");Number(actual.DowntimeMinutes,ap+".downtimeMinutes");
                    if(actual.StartedAt.HasValue&&actual.EndedAt.HasValue&&actual.DowntimeMinutes>(decimal)(actual.EndedAt.Value-actual.StartedAt.Value).TotalMinutes)Error(ap+".downtimeMinutes","Duruş süresi işlem süresini aşamaz.");
                    if(actual.DowntimeReason?.Length>1000)Error(ap+".downtimeReason","Duruş nedeni en fazla 1000 karakterdir.");
                    if(actual.DowntimeMinutes>0&&string.IsNullOrWhiteSpace(actual.DowntimeReason))Error(ap+".downtimeReason","Duruş nedenini belirtin.");
                    if(actual.Lines is not {Count:>0} || actual.Lines.Count>10000){Error(ap+".lines","Gerçekleşme dağılımı gereklidir.");continue;}
                    var actualKeys=new HashSet<(Guid,Guid,Guid?)>();
                    foreach(var (line,li) in actual.Lines.Select((x,i)=>(x,i)))
                    {
                        var lp=$"{ap}.lines[{li}]";
                        if(line is null){Error(lp,"Satır boş olamaz.");continue;}
                        var key=(line.VariantOptionId,line.SizeOptionId,line.SetPartOptionId);
                        if(!planKeys.Contains(key)||!actualKeys.Add(key))Error(lp,"Pastalda bulunan, tekrarsız beden/varyant/parça seçin.");
                        Number(line.CutQuantity,lp+".cutQuantity",true);Number(line.GoodQuantity,lp+".goodQuantity",true);Number(line.DefectQuantity,lp+".defectQuantity",true);
                        if((line.GoodQuantity??0)+(line.DefectQuantity??0)>line.CutQuantity || (line.GoodQuantity.HasValue&&line.DefectQuantity.HasValue&&line.GoodQuantity+line.DefectQuantity!=line.CutQuantity))
                            Error(lp,"Sağlam ve hatalı adetler kesilen adedi aşamaz; iki sınıf girildiyse toplam kesilene eşit olmalıdır.");
                    }
                }
            }
        }
        if(totalPlans>1000||totalActuals>5000)Error("groups","Bir kayıtta en fazla 1000 pastal ve 5000 gerçekleşme bulunabilir.");
        if(errors.Count>0)throw new CuttingValidationException(errors);
    }
}
