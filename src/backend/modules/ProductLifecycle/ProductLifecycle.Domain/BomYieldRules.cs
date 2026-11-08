namespace ProductLifecycle.Domain;
public static class BomYieldRules
{
    public static decimal Gross(decimal net,decimal waste)
    {
        if(net<0 || waste<0 || waste>=1)throw new ArgumentException("Net miktar negatif olamaz; hesaplanabilir fire 0 ile 1 arasında olmalıdır.");
        return checked(net/(1-waste));
    }
}
