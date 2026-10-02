using IpsosPagoHonorarios.Web.Services;

namespace IpsosPagoHonorarios.Web.Pages.Cuenta;

/// <summary>Activación de la cuenta del portal con el enlace de un solo uso (72 h).</summary>
public class ActivarModel(PrestadoresService prestadores) : RecuperarModel(prestadores)
{
    public override bool Activacion => true;
}
