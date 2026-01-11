using Microsoft.Extensions.Configuration;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

public class WhatsAppService
{
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly string _from;

    public WhatsAppService(IConfiguration configuration)
    {
        _accountSid = configuration["Twilio:AccountSid"];
        _authToken = configuration["Twilio:AuthToken"];
        _from = configuration["Twilio:WhatsAppFrom"];

        TwilioClient.Init(_accountSid, _authToken);
    }

    public string EnviarMensaje(string toPhone, string mensaje)
    {
        try
        {
            if (!toPhone.StartsWith("+"))
            {
                toPhone = "+1" + toPhone;  // Asumiendo que el número es de EE.UU.
            }
            // Enviar mensaje de WhatsApp
            var message = MessageResource.Create(
                from: new PhoneNumber(_from),  // Número de WhatsApp desde el que se enviará
                 to: new PhoneNumber(toPhone),  // Número del destinatario en WhatsApp
                body: mensaje
            );

            return message.Sid;  // Retorna el SID del mensaje enviado
        }
        catch (Exception ex)
        {
            // Manejo de errores
            return $"Error: {ex.Message}";
        }
    }
}
