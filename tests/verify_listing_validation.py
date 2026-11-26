"""Verify Persian client-side listing validation on a seeded development app."""
import os
import sys

from playwright.sync_api import expect, sync_playwright

root = sys.argv[1].rstrip("/")
message = "لطفاً توضیحات محصول را بین ۱۰ تا ۴۰۰۰ کاراکتر وارد کنید."

with sync_playwright() as playwright:
    browser = playwright.chromium.launch(channel=os.getenv("BROWSER_CHANNEL", "msedge"))
    for width, path in [(390, "/Listings/Edit"), (1440, "/Listings/Edit/1")]:
        page = browser.new_page(viewport={"width": width, "height": 850})
        page.goto(root + "/Account/Login?returnUrl=" + path)
        page.locator("#Email").fill("sara@handmade.local")
        page.locator("#Password").fill(os.environ["Seed__DemoPassword"])
        page.get_by_role("button", name="ورود", exact=True).click()
        expect(page).to_have_url(root + path)
        expect(page.locator("html")).to_have_attribute("dir", "rtl")
        page.locator("#Title").fill("محصول آزمایشی")
        page.locator("#City").fill("قزوین")
        page.locator("#CategoryId").select_option(index=1)
        page.locator("#Price").fill("۱٬۰۰۰")
        posts = []
        page.on("request", lambda request: posts.append(request.url) if request.method == "POST" else None)
        for length in (9, 4001):
            page.locator("#Description").evaluate("(field, value) => { field.value = value; }", "ا" * length)
            page.get_by_role("button", name="ذخیره و ارسال برای بررسی").click()
            expect(page.locator(".validation-summary-errors li")).to_have_text(message)
            assert not posts
            assert page.evaluate("document.documentElement.scrollWidth <= innerWidth")
        for length in (10, 4000):
            page.locator("#Description").fill("ا" * length)
            assert page.locator("#Description").evaluate("(input) => $(input).valid()")
        assert page.locator("#Price").evaluate("(input) => $(input).valid()")
        page.close()
    browser.close()

print("Persian listing validation passed on mobile create and desktop edit.")
