// The only simulated boundary is TMDB. Arr, Seerr, Jellyfin and LibManagarr remain real.
// Unknown endpoints fail closed so additions cannot silently receive invented metadata.
import https from 'node:https';
import { readFileSync } from 'node:fs';

const movie = {
  id: 27205,
  imdb_id: 'tt1375666',
  title: 'Inception',
  original_title: 'Inception',
  original_language: 'en',
  overview: 'Synthetic E2E metadata for the request scenario.',
  release_date: '2010-07-16',
  runtime: 148,
  status: 'Released',
  adult: false,
  video: false,
  popularity: 1,
  vote_average: 8,
  vote_count: 1,
  poster_path: null,
  backdrop_path: null,
  belongs_to_collection: null,
  genres: [],
  production_companies: [],
  production_countries: [],
  spoken_languages: [],
  credits: { cast: [], crew: [] },
  external_ids: { imdb_id: 'tt1375666' },
  release_dates: { results: [] },
  videos: { results: [] },
  keywords: { keywords: [] },
  recommendations: { results: [], page: 1, total_pages: 0, total_results: 0 },
  similar: { results: [], page: 1, total_pages: 0, total_results: 0 },
  'watch/providers': { results: {} },
  images: { backdrops: [], posters: [] },
};
https
  .createServer({ key: readFileSync('/certs/metadata.key'), cert: readFileSync('/certs/metadata.crt') }, (req, res) => {
    const path = new URL(req.url, 'https://api.themoviedb.org').pathname;
    console.log(`${req.method} ${path}`); // Never log TMDB query credentials.
    res.setHeader('Content-Type', 'application/json');
    if (req.method === 'GET' && path === '/3/movie/27205') return res.end(JSON.stringify(movie));
    res.statusCode = 404;
    res.end(JSON.stringify({ status_message: `No E2E fixture for ${path}` }));
  })
  .listen(443, '0.0.0.0');
