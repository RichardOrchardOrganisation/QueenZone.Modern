import unittest

from build import concert_source_urls


class ConcertSourceUrlTests(unittest.TestCase):
    def test_extracts_and_deduplicates_real_concert_sources(self):
        url = 'https://www.queenconcerts.com/detail/123.html'
        self.assertEqual(concert_source_urls(f'Source: {url}\n{url}'), {url})

    def test_rejects_lookalike_hostnames(self):
        for host in ('wwwXqueenconcerts.com', 'www.queenconcertsXcom',
                     'www.queenconcerts.com.attacker.example'):
            with self.subTest(host=host):
                self.assertEqual(concert_source_urls(f'https://{host}/detail/123.html'), set())


if __name__ == '__main__':
    unittest.main()
