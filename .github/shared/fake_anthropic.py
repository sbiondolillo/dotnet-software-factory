"""The fake of the Anthropic adapter of anthropic.py, for the unit tests of sessions.py, the judge and the reader.

It holds the API of one session. A POST records its body. A GET of the events gives the next listing of `listings`,
newest first, and the last one again when they run out. A listing that is a dict is the whole page, with its
`next_page`. A GET of the session gives `status`, and `posted` in its place once a POST to the session's events came.
"""

SID = "sesn_1"


def paged(events, limit=100):
    """The listings of `events`, newest first, in pages of `limit` events, each page naming the next one by the index
    of its first event, as the API's `next_page` does."""
    pages = []
    for start in range(0, max(len(events), 1), limit):
        end = start + limit
        pages.append({"data": events[start:end], "next_page": str(end) if end < len(events) else None})
    return pages


class FakeApi:
    """The surface of anthropic.Anthropic for one session `sid`."""

    def __init__(self, listings=([],), status="idle", posted=None, sid=SID):
        self.posts = []
        self.listings = list(listings)
        self.status = status
        self.posted = posted
        self.sid = sid
        self.reads = 0
        self.paths = []

    def get(self, path):
        if path == f"/v1/sessions/{self.sid}":
            return {"status": self.status}
        self.reads += 1
        self.paths.append(path)
        listing = self.listings.pop(0) if len(self.listings) > 1 else self.listings[0]
        return listing if isinstance(listing, dict) else {"data": listing}

    def post(self, path, body):
        self.posts.append((path, body))
        if path == "/v1/sessions":
            return {"id": self.sid}
        if self.posted is not None:
            self.status = self.posted
        return {}
