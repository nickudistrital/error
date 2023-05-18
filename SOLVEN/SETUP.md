# Daemonize the executable file

## Create a /etc/init.d script that will handle a daemon

Navigate to /etc/init.d/ folder and create a new file:

```
cd /etc/init.d
sudo nano solven
```

Copy and paste the following into `/etc/init.d/solven`.
Replace `APP_USER` with the Raspberry Pi user.
Modify the `APP_PATH` with the location path of the program.

```
#!/bin/sh
#/etc/init.d/solven

APP_NAME="solven.sh"
APP_USER=pi
APP_PATH="/home/${APP_USER}/SOLVEN"


case "$1" in
  start)


        echo "Starting $APP_NAME"

        start-stop-daemon --start \
                          --background \
                          --make-pidfile \
                          --pidfile /var/run/$APP_NAME.pid \
                          --chuid $APP_USER \
                          --exec "$APP_PATH/$APP_NAME"
    ;;
  stop)

        echo "Stopping $APP_NAME"
                start-stop-daemon -o  --stop \
                --pidfile /var/run/$APP_NAME.pid

    ;;
  *)
    echo "Usage: /etc/init.d/$APP_NAME {start|stop}"
    exit 1
    ;;
esac

exit 0
```

## Create a second script inside the project folder that will keep running the

mono app and save the output into a log file.
The log file is located at the same path as the `SOLVEN.exe` file.
This script is called `solven.sh` and should already be included when you
download the project repository.

## Create a root crontab file that will start the `/etc/init.d/solven` service upon OS boot up

Create/edit the crontab file:

    sudo crontab -e

Add the following line at the bottom:

    @reboot sudo /etc/init.d/solven start

**Source**: <https://stackoverflow.com/a/1234761/7436540>

# Setup Gmail SMTP on Ubuntu

First of all you need to install and configure Postfix to Use Gmail SMTP on Ubuntu.

Install all necessary packages:

    sudo apt-get install postfix mailutils libsasl2-2 ca-certificates libsasl2-modules

If you do not have postfix installed before, postfix configuration wizard will ask you some questions. Just select your server as **Internet Site** and for FQDN use something like **mail.example.com**

Then open your ***postfix config file***:

    sudo -H gedit /etc/postfix/main.cf

and add following lines to it:

    relayhost = [smtp.gmail.com]:587
    smtp_sasl_auth_enable = yes
    smtp_sasl_password_maps = hash:/etc/postfix/sasl_passwd
    smtp_sasl_security_options = noanonymous
    smtp_tls_CAfile = /etc/postfix/cacert.pem
    smtp_use_tls = yes

You might have noticed that we haven’t specified our Gmail username and password in above lines. They will go into a different file. Open/Create:

    sudo -H gedit /etc/postfix/sasl_passwd

And add following line:

    [smtp.gmail.com]:587    USERMAIL@gmail.com:PASSWORD

If you want to use your Google App’s domain, please replace **@gmail.com** with your **@domain.com**.

Fix permission and update postfix config to use sasl_passwd file:

    sudo chmod 400 /etc/postfix/sasl_passwd
    sudo postmap /etc/postfix/sasl_passwd

Next, validate certificates to avoid running into error. Just run following command:

    cat /etc/ssl/certs/Thawte_Premium_Server_CA.pem | sudo tee -a /etc/postfix/cacert.pem

Finally, reload postfix config for changes to take effect:

    sudo /etc/init.d/postfix reload

## Testing

### Check if mails are sent via Gmail SMTP server

If you have configured everything correctly, following command should generate a test mail from your server to your mailbox.

    echo "Test mail from postfix" | mail -s "Test Postfix" you@example.com

To further verify, if mail sent from above command is actually sent via Gmail’s SMTP server, you can log into Gmail account USERNAME@gmail.com with PASSWORD and check "***Sent Mail***" folder in that Gmail account. By default, Gmail always keeps a copy of mail being sent through its web-interface as well as SMTP server. This logging is one strong reason that we often use Gmail when mail delivery is critical.

## Troubleshooting

### Error: "SASL authentication failed; server smtp.gmail.com"

You need to unlock the captcha by visiting this page
<https://www.google.com/accounts/DisplayUnlockCaptcha>

You can run test again after unlocking captcha
[source](https://rtcamp.com/tutorials/linux/ubuntu-postfix-gmail-smtp/)
---

You need to use following syntax of `mail` and `mutt` to send emails, note that if you want to send attachment file via `mail` command it's not support or it's better I say I can not send my attached file via `mail` command, instead you can use `mutt` command line, it's very useful. and in `mutt` command you have to type attachment arguments after the email address. I test it and works fine.

you can install `mutt` via this command:

    sudo apt-get install mutt

__Using `mail`__

    mail -s "TestSubject" nospam@gmail.com -a "UserReport.txt"  < MessageBody.txt
__Using `mutt`__

    mutt -s "TestSubject" nospam@gmail.com -a "UserReport.txt"  < MessageBody.txt

While `UserReport.txt` is your attachment file, `MessageBody` is text/file of your body of email, `TestSubject` is your email subject.

`-s` flag is used for "Subject" and `-a` flag is used for "Attachment file"

**Source**: <https://askubuntu.com/a/522434/921554>
