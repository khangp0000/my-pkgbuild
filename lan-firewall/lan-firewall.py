#!/usr/bin/env python3

import ipaddress
import socket
import psutil
import math
import xmltodict
import shutil
import subprocess
import tempfile
import os

def move_with_perm(src, dst):
    stat = os.stat(dst)
    shutil.copymode(dst, src)
    shutil.move(src, dst)
    shutil.chown(dst, stat.st_uid, stat.st_gid)

addrs = psutil.net_if_addrs()['enp4s0']
sanitized_addrs = []
for addr in addrs:
    if addr.family == socket.AF_INET or addr.family == socket.AF_INET6:
        val = int.from_bytes(ipaddress.ip_address(addr.netmask).packed, byteorder='little')
        prefix_len = int(math.log2(val + 1))
        cidr = ipaddress.ip_network(f"{addr.address}/{prefix_len}", strict=False)
        if cidr.version == 4 or cidr.is_global:
            sanitized_addrs.append(str(cidr))
            
ipset = set(sanitized_addrs)

with open('/etc/firewalld/zones/home.xml', "r") as fd:
    firewalld_cf = xmltodict.parse(fd.read())

if isinstance(firewalld_cf['zone']['source'], dict):
    currentAddrs = set(firewalld_cf['zone']['source']['@address'])
elif isinstance(firewalld_cf['zone']['source'], list):
    currentAddrs = set(k['@address'] for k in firewalld_cf['zone']['source'])

if (currentAddrs != ipset):
    firewalld_cf['zone']['source'] = map(lambda s: {'@address': s}, sanitized_addrs)

    print("Updating firewalld config...")
    with tempfile.NamedTemporaryFile(mode='w', prefix="lan-firewall", delete=False) as temp_firewalld:
        temp_firewalld.write(xmltodict.unparse(firewalld_cf,short_empty_elements=True))
    move_with_perm(temp_firewalld.name, "/etc/firewalld/zones/home.xml")
    print("Reload firewalld...")
    subprocess.run(['/usr/bin/firewall-cmd', '--reload'], check=True)
    print("Reload fail2ban...")
    subprocess.run(['systemctl', 'restart', 'fail2ban.service'], check=True)
else:
    print("Firewalld does not need update...")