#!/bin/bash
# 在 N100 上跑：把 /tmp 里传上来的东西装好。
#   bash install-server.sh [安装目录]
#
# 幂等，重复跑没副作用：网页文件覆盖、服务端二进制覆盖、systemd 重启、容器 up -d。
set -u

DIR=${1:-/opt/go-bot/fankou}

# 传上来的东西用完就删：那个服务端二进制 97MB、网页包 15MB，
# 留在 /tmp 里没人管，攒几次就把磁盘啃掉一块。
# 用 trap 是为了**失败也清**——之前就是只写成功路径，结果失败一次就留一堆。
cleanup() {
    rm -f /tmp/fankou-web.tgz /tmp/FanKou.Server /tmp/nginx.conf /tmp/docker-compose.yml
}
trap cleanup EXIT

echo "=== 1/5 网页 ==="
mkdir -p "$DIR/wwwroot"
rm -rf "${DIR:?}/wwwroot"/*
tar -xzf /tmp/fankou-web.tgz -C "$DIR/wwwroot"
find "$DIR/wwwroot" -type f | wc -l

echo "=== 2/5 联机服务端 ==="
mkdir -p "$DIR/server"

# 覆盖正在运行的可执行文件会 "Text file busy"：先落到旁边再 mv，
# mv 是换目录项，跑着的进程继续用旧 inode，不受影响。
cp /tmp/FanKou.Server "$DIR/server/FanKou.Server.new"
chmod +x "$DIR/server/FanKou.Server.new"
mv -f "$DIR/server/FanKou.Server.new" "$DIR/server/FanKou.Server"

sudo tee /etc/systemd/system/fankou-server.service >/dev/null <<'EOF'
[Unit]
Description=FanKou online game server (双扣联机服务端)
After=network.target

[Service]
WorkingDirectory=/opt/go-bot/fankou/server
ExecStart=/opt/go-bot/fankou/server/FanKou.Server
Environment=FANKOU_URLS=http://0.0.0.0:8081
Restart=always
RestartSec=3
User=fushiyimeng

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable fankou-server >/dev/null 2>&1
sudo systemctl restart fankou-server

echo "=== 3/5 nginx + 容器 ==="
cp /tmp/nginx.conf "$DIR/nginx.conf"
cp /tmp/docker-compose.yml "$DIR/docker-compose.yml"
cd "$DIR" && docker compose up -d 2>&1 | tail -2

echo "=== 4/5 443 上的 vhost（没有就补） ==="
python3 - <<'PY'
import io, os
p = '/opt/go-bot/portal/edge.conf'
if not os.path.exists(p):
    print('edge.conf 不存在，跳过')
    raise SystemExit(0)

s = io.open(p, encoding='utf-8').read()

if 'server_name fankou.fushiyimeng.cn;' not in s:
    block = '''
# ── 双扣网页版（联机）──────────────────────────────────────────
# 静态页在自己的容器 fankou-web（专端口 8080）上；/ws 由它转给宿主机上的
# 游戏服务端（systemd: fankou-server，127.0.0.1:8081）。两个容器都在
# go-bot_astrbot_network 上，按容器名反代。
# ⚠️ 故意不加 HSTS：加了会把 http://fankou.fushiyimeng.cn:8080 也升级成 https，
#    而那个口是明文 HTTP，等于自己把直连口锁死。
server {
    listen      443 ssl;
    listen      [::]:443 ssl;
    server_name fankou.fushiyimeng.cn;

    ssl_certificate     /certs/le/fullchain.pem;
    ssl_certificate_key /certs/le/key.pem;
    ssl_protocols       TLSv1.2 TLSv1.3;
    ssl_session_cache   shared:SSL:2m;
    access_log /dev/stdout gobot;

    location / {
        proxy_pass http://fankou-web:80;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_read_timeout 3600s;
    }
}
'''
    io.open(p, 'a', encoding='utf-8').write(block)
    print('vhost 已追加')
else:
    i = s.find('server_name fankou.fushiyimeng.cn;')
    j = s.find('proxy_pass http://fankou-web:80;', i)
    k = s.find('}', j)
    if j > 0 and 'Upgrade' not in s[j:k]:
        add = '\n        proxy_set_header Upgrade $http_upgrade;\n        proxy_set_header Connection $connection_upgrade;'
        s = s[:j] + s[j:k].replace('proxy_http_version 1.1;', 'proxy_http_version 1.1;' + add, 1) + s[k:]
        io.open(p, 'w', encoding='utf-8').write(s)
        print('vhost 补上了 WebSocket 升级头')
    else:
        print('vhost 已经是对的')
PY

sudo docker exec portal nginx -t 2>&1 | tail -2
sudo docker exec portal nginx -s reload 2>&1
sleep 2

echo "=== 5/5 自检 ==="
systemctl is-active fankou-server
curl -s -m 5 http://127.0.0.1:8081/health; echo
curl -s -o /dev/null -w 'web 8080: %{http_code}\n' http://127.0.0.1:8080/
curl -sk -o /dev/null -w '443: %{http_code}\n' -H 'Host: fankou.fushiyimeng.cn' https://127.0.0.1/
# ⚠️ 这条 curl 拿到 101 之后自己会以非零码退出（它不懂协议切换），
#    别让它把整个部署判成失败——所以后面显式 exit 0。
curl -sk -m 6 -o /dev/null -w 'wss handshake: %{http_code}\n' \
  -H 'Host: fankou.fushiyimeng.cn' \
  -H 'Connection: Upgrade' -H 'Upgrade: websocket' \
  -H 'Sec-WebSocket-Version: 13' -H 'Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==' \
  https://127.0.0.1/ws || true

echo "部署完成"
exit 0
