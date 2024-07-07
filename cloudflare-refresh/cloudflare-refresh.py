#!/usr/bin/env python3

import requests
import xmltodict
import tempfile
import shutil
import subprocess
import os

def move_with_perm(src, dst):
    stat = os.stat(dst)
    shutil.copymode(dst, src)
    shutil.move(src, dst)
    shutil.chown(dst, stat.st_uid, stat.st_gid)
    

print("Downloading cloudflare ips...")
ipsv4 = requests.get("https://www.cloudflare.com/ips-v4/").text.splitlines()
ipsv6 = requests.get("https://www.cloudflare.com/ips-v6/").text.splitlines()

ips = map(lambda s: {'@address': s}, ipsv4 + ipsv6)

with open('/etc/firewalld/zones/cloudflare.xml', "r") as fd:
    firewalld_cf = xmltodict.parse(fd.read())
    
firewalld_cf['zone']['source'] = ips

print("Updating firewalld config...")
with tempfile.NamedTemporaryFile(mode='w', prefix="cloudflare-refresh", delete=False) as temp_firewalld:
    temp_firewalld.write(xmltodict.unparse(firewalld_cf,short_empty_elements=True))
move_with_perm(temp_firewalld.name, "/etc/firewalld/zones/cloudflare.xml")

print("Updating nginx config...")
with tempfile.NamedTemporaryFile(mode='w', prefix="cloudflare-refresh", delete=False) as temp_nginx:
    temp_nginx.writelines(map(lambda s: f"set_real_ip_from {s};\n", ipsv4 + ipsv6))
move_with_perm(temp_nginx.name, "/etc/nginx/http/locations/cloudflare_ips.conf")

print("Reload firewalld...")
subprocess.run(['/usr/bin/firewall-cmd', '--reload'], check=True)
print("Reload fail2ban...")
subprocess.run(['systemctl', 'restart', 'fail2ban.service'], check=True)
print("Reload nginx...")
subprocess.run(['nginx', '-s', 'reload'], check=True)