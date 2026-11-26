"""Read-only SEO smoke checks against a seeded local app or the live marketplace."""

import argparse
import json
import re
from html.parser import HTMLParser
from urllib.error import HTTPError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener
from xml.etree import ElementTree

ORIGIN = "https://khodamsaz.ir"
NAMESPACE = "{http://www.sitemaps.org/schemas/sitemap/0.9}"


class Head(HTMLParser):
    def __init__(self, markup):
        super().__init__()
        self.meta, self.schema = {}, []
        self.title, self.canonical, self.lang = "", None, None
        self.reading_title = False
        self.reading_schema = False
        self.feed(markup)

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "meta" and "content" in attrs:
            self.meta[attrs.get("name") or attrs.get("property")] = attrs["content"]
        elif tag == "link" and attrs.get("rel") == "canonical":
            self.canonical = attrs["href"]
        elif tag == "title":
            self.reading_title = True
        elif tag == "script" and attrs.get("type") == "application/ld+json":
            self.reading_schema = True
        elif tag == "html":
            self.lang = (attrs.get("lang"), attrs.get("dir"))

    def handle_endtag(self, tag):
        if tag == "title":
            self.reading_title = False
        elif tag == "script":
            self.reading_schema = False

    def handle_data(self, data):
        if self.reading_title:
            self.title += data
        if self.reading_schema:
            self.schema.append(json.loads(data))


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("root")
    parser.add_argument("--redirect-from", action="append", default=[])
    args = parser.parse_args()
    root = args.root.rstrip("/")
    client = build_opener(ProxyHandler({}), NoRedirect())

    def request(path, status=200):
        url = path if path.startswith(("http://", "https://")) else root + path
        try:
            response = client.open(Request(url), timeout=15)
        except HTTPError as error:
            response = error
        with response:
            assert response.status == status, (url, response.status, status)
            return response.headers, response.read()

    def document(path):
        return Head(request(path)[1].decode("utf-8"))

    home = document("/")
    assert home.lang == ("fa", "rtl") and "خودم‌ساز" in home.title
    assert home.canonical == ORIGIN + "/" and home.meta["og:url"] == home.canonical
    assert "دست‌ساز" in home.meta["description"]
    assert "robots" not in home.meta
    home_markup = request("/")[1].decode()
    versioned_css = re.search(r'href="(/css/site\.css\?v=[^"]+)"', home_markup).group(1)
    assert "immutable" in request(versioned_css)[0]["Cache-Control"]
    graph = home.schema[0]
    assert graph["@context"] == "https://schema.org"
    assert {node["@type"] for node in graph["@graph"]} == {"Organization", "WebSite"}
    assert document("/Home/Index").canonical == home.canonical
    assert "noindex" in document("/?Q=example").meta["robots"]
    assert "noindex" in document("/?page=99999").meta["robots"]
    assert "noindex" in document("/?utm_source=example").meta["robots"]
    assert "noindex" in document("/Account/Login").meta["robots"]
    for route, status in [("/Account/Login", 200), ("/Messages", 302), ("/Admin", 302)]:
        assert request(route, status)[0]["X-Robots-Tag"] == "noindex"
    request("/Listings/Details/2147483647", 404)
    request("/categories/2147483647", 404)

    robots = request("/robots.txt")[1].decode()
    assert "User-agent: *" in robots and "Sitemap: " + ORIGIN + "/sitemap.xml" in robots
    headers, content = request("/sitemap.xml")
    assert headers.get_content_type() == "application/xml"
    sitemap = ElementTree.fromstring(content)
    assert sitemap.tag == NAMESPACE + "urlset"
    urls = [entry.text for entry in sitemap.findall(".//" + NAMESPACE + "loc")]
    assert ORIGIN + "/" in urls and len(urls) == len(set(urls))
    assert all(re.fullmatch(r"https://khodamsaz\.ir/(?:Listings/(?:Details|Maker)/[^/?]+|categories/\d+|qazvin)?", url)
               for url in urls)
    paths = [urlsplit(url).path for url in urls]
    products = [path for path in paths if path.startswith("/Listings/Details/")]
    assert products and any(path.startswith("/categories/") for path in paths)

    for path in paths:
        page = document(path)
        assert page.canonical == ORIGIN + path and "robots" not in page.meta, path
        if path == "/":
            continue
        assert page.meta["og:url"] == page.canonical
        nodes = page.schema[0] if isinstance(page.schema[0], list) else [page.schema[0]]
        assert all(node["@context"] == "https://schema.org" for node in nodes)
        crumbs = next(node["itemListElement"] for node in nodes if node["@type"] == "BreadcrumbList")
        assert [item["position"] for item in crumbs] == list(range(1, len(crumbs) + 1))
        assert crumbs[0]["item"] == ORIGIN + "/" and crumbs[-1]["item"] == page.canonical
        assert all(item["@type"] == "ListItem" and item["name"] and item["item"] in urls for item in crumbs)
        if path not in products:
            continue
        product = next(node for node in nodes if node["@type"] == "Product")
        assert product["name"] in page.title and page.meta["description"] == product["description"]
        assert product["url"] == page.canonical
        assert not {"aggregateRating", "review", "availability"}.intersection(product)
        if "offers" in product:
            offer = product["offers"]
            assert offer["@type"] == "Offer" and offer["priceCurrency"] == "IRR" and offer["price"] > 0
            assert offer["url"] == page.canonical and "availability" not in offer
        for image in product.get("image", []):
            assert image.startswith(ORIGIN + "/uploads/")
            assert request(urlsplit(image).path)[0].get_content_type().startswith("image/")
        duplicate = document(path + "?utm_medium=example")
        assert duplicate.canonical == page.canonical and "noindex" in duplicate.meta["robots"]

    if args.redirect_from:
        suffix = products[0] + "?seo-check=1&preserve=2"
        for alias in args.redirect_from:
            assert request(alias + suffix, 301)[0]["Location"] == ORIGIN + suffix
        assert "noindex" in document(suffix).meta["robots"]
    print(f"SEO checks passed for {len(paths)} sitemap entries and {len(products)} product pages.")


if __name__ == "__main__":
    main()
