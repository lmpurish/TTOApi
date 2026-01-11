using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using MimeKit;
using TToApp.Model;

public class EmailService
{
    private readonly EmailSettings _emailSettings;
    private readonly IWebHostEnvironment _env;

    public EmailService(IOptions<EmailSettings> emailSettings, IWebHostEnvironment env)
    {
        _emailSettings = emailSettings.Value;
        _env = env;
    }

    public async Task<bool> SendEmailAsync(
        string toEmail,
        string subject,
        string templateFileName,
        Dictionary<string, string> placeholders = null,
        List<string> attachmentPaths = null,
        bool copy = false)
    {
        using var smtp = new SmtpClient();

        try
        {
            // Validación de correo
            if (string.IsNullOrWhiteSpace(toEmail) || !MailboxAddress.TryParse(toEmail, out _))
            {
                Console.WriteLine("Correo de destino inválido.");
                return false;
            }

            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(_emailSettings.SenderName, _emailSettings.SenderEmail));
            email.To.Add(new MailboxAddress("", toEmail));

            if (copy)
            {
                email.Cc.Add(new MailboxAddress("Owner TTO Logistics", "torrestransportone@gmail.com"));
                email.Cc.Add(new MailboxAddress("Admin TTO Logistics", "tto1coo@gmail.com"));
            }

            email.Subject = subject;

            var bodyBuilder = new BodyBuilder();

            // 🔍 Ruta absoluta a la plantilla
            string templatePath = Path.Combine(_env.ContentRootPath, "Templates", templateFileName);
            string htmlBody;

            if (File.Exists(templatePath))
            {
                htmlBody = await File.ReadAllTextAsync(templatePath);
            }
            else
            {
                htmlBody = $"[ERROR] Plantilla no encontrada: {templatePath}";
                Console.WriteLine(htmlBody);
            }

            // Reemplazar placeholders
            if (placeholders != null)
            {
                foreach (var placeholder in placeholders)
                {
                    htmlBody = htmlBody.Replace($"{{{{{placeholder.Key}}}}}", placeholder.Value);
                }
            }

            bodyBuilder.HtmlBody = htmlBody;
            bodyBuilder.TextBody = Regex.Replace(htmlBody, "<.*?>", string.Empty); // Versión texto

            // Adjuntos
            if (attachmentPaths != null)
            {
                foreach (var path in attachmentPaths)
                {
                    if (File.Exists(path))
                    {
                        bodyBuilder.Attachments.Add(path);
                    }
                }
            }

            email.Body = bodyBuilder.ToMessageBody();

            // Enviar
            await smtp.ConnectAsync(_emailSettings.SmtpServer, _emailSettings.SmtpPort, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(_emailSettings.SmtpUser, _emailSettings.SmtpPass);
            await smtp.SendAsync(email);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al enviar correo: {ex.Message}");
            return false;
        }
        finally
        {
            if (smtp.IsConnected)
                await smtp.DisconnectAsync(true);
        }
    }
}
