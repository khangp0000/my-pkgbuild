
#!/usr/bin/env bash
set -e

# remove all public rules first
IFS=$' '
for i in $(firewall-cmd --list-sources --zone=cloudflare); do
        echo "removing '$i'"
        firewall-cmd --permanent --zone=cloudflare --remove-source=$i
done

# add new rules

# Download whitelist

ipv4_cloudflare=$(curl -L -f "https://www.cloudflare.com/ips-v4")


# IPv4 HTTP
IFS=$'\n'
echo "adding IPv4 to cloudflare zone"
for i in $ipv4_cloudflare; do
        echo "adding '$i'"
        firewall-cmd --permanent --zone=cloudflare --add-source=$i;
done

# Nginx config whitelist
echo "Updating nginx conf"
nginx_tmpfile=$(mktemp)
for i in $ipv4_cloudflare; do
        echo "set_real_ip_from $i;" >> $nginx_tmpfile
done
mv $nginx_tmpfile /etc/nginx/http/locations/cloudflare_ips.conf

set +e
echo "reloading..."
firewall-cmd --reload
systemctl restart fail2ban.service
nginx -s reload

exit 0
