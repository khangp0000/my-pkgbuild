
#!/usr/bin/env bash

# remove all public rules first
IFS=$'\n'
for i in $(firewall-cmd --list-rich-rules --zone=public | grep 'protocol="tcp" accept'); do
        echo "removing '$i'"
        firewall-cmd --permanent --zone=public --remove-rich-rule "$i"
done

#echo "reloading..."
#sudo firewall-cmd --reload
#exit 1

# add new rules

ipv4_cloudflare=$(curl "https://www.cloudflare.com/ips-v4")

# IPv4 HTTP
echo "adding IPv4 HTTP"
for i in $ipv4_cloudflare; do
        echo "adding '$i'"
        firewall-cmd --permanent --zone=public --add-rich-rule 'rule family="ipv4" source address="'$i'" port port=80 protocol=tcp accept';
done

# IPv4 HTTPS
echo "adding IPv4 HTTPS"
for i in $ipv4_cloudflare; do
        echo "adding '$i'"
        firewall-cmd --permanent --zone=public --add-rich-rule 'rule family="ipv4" source address="'$i'" port port=443 protocol=tcp accept';
done

# Local Lan IP
firewall-cmd --permanent --zone=public --add-rich-rule 'rule family="ipv4" source address="'192.168.1.0/24'" port port=80 protocol=tcp accept';
firewall-cmd --permanent --zone=public --add-rich-rule 'rule family="ipv4" source address="'192.168.1.0/24'" port port=443 protocol=tcp accept';

# SSH
#firewall-cmd --permanent --zone=public --add-rich-rule 'rule family="ipv4" source address="myip" port port=22 protocol=tcp accept'
#firewall-cmd --permanent --change-zone=eth0 --zone=public

echo "Updating nginx conf"
nginx_tmpfile=$(mktemp)
for i in $ipv4_cloudflare; do
        echo "set_real_ip_from $i;" >> $nginx_tmpfile
done
mv $nginx_tmpfile /etc/nginx/http/locations/cloudflare_ips.conf

echo "reloading..."
firewall-cmd --reload || true
systemctl restart fail2ban.service || true
nginx -s reload || true