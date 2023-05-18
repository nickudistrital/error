using System;
using System.Net;
using System.Net.Mail;

namespace EmailSender
{
    public class EmailService
    {
        private const string HOST = "smtp.gmail.com";
        private const int PORT = 587;
        private const string SENDER = "solven.smtp@gmail.com";
        private const string PASSWORD = "FeRuOsHs108";

        public bool SendEmail(string mailTo, string subject, string body)
        {
            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            MailMessage mail = new MailMessage(SENDER, mailTo);
            SmtpClient client = new SmtpClient
            {
                Port = PORT,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Host = HOST,
                Credentials = new NetworkCredential(SENDER, PASSWORD),
                EnableSsl = true
            };
            mail.Subject = subject;
            mail.Body = body;
            mail.BodyEncoding = System.Text.Encoding.UTF8;
            mail.SubjectEncoding = System.Text.Encoding.UTF8;
            try
            {
                client.Send(mail);
                mail.Dispose();

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
