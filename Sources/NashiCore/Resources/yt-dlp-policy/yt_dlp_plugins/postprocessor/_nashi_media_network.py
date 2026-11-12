"""Keep yt-dlp request handlers and Python sockets on the app's media proxy."""

import sys
from urllib.parse import urlsplit

from yt_dlp.networking.common import RequestDirector, RequestHandler
from yt_dlp.networking.exceptions import UnsupportedRequest


def install_network_policy(proxy):
    """Force the per-run SOCKS endpoint and refuse direct Python socket connections."""
    parsed = urlsplit(proxy)
    if parsed.scheme != 'socks5h' or parsed.hostname != '127.0.0.1' or not parsed.port or not parsed.password:
        raise ValueError('Nashi media policy requires its local SOCKS endpoint')
    endpoint = (parsed.hostname, parsed.port)
    send = RequestDirector.send
    # The pinned handlers use this hook to configure urllib, requests, and libcurl.
    getattr(RequestHandler, '_get_proxies')

    def public_request(self, request):
        """Discard extractor-specific proxy overrides before choosing a handler."""
        if urlsplit(request.url).scheme.lower() not in {'http', 'https', 'ws', 'wss', 'data'}:
            raise UnsupportedRequest('Nashi media requests must use HTTP(S), WebSockets, or inline data')
        request.headers.pop('Ytdl-Request-Proxy', None)
        request.proxies = {'all': proxy}
        return send(self, request)

    def proxy_only(self, request):
        """Supply the same endpoint even when a handler asks for proxies again."""
        return {'all': proxy}

    def socket_policy(event, arguments):
        """Block socket-based extractors from bypassing the proxy."""
        if event in {'socket.connect', 'socket.sendto', 'socket.sendmsg'} and arguments[-1] != endpoint:
            raise PermissionError('Nashi media tools must connect through the local SOCKS proxy')

    RequestDirector.send = public_request
    RequestHandler._get_proxies = proxy_only
    sys.addaudithook(socket_policy)
